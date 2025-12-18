using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using GMap.NET;
using GMap.NET.WindowsForms;
using Newtonsoft.Json.Linq;

namespace Possible_routes_of_missing_people
{
    public class POI
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public PointLatLng Location { get; set; }
        public double Distance { get; set; }
    }

    public class RouteFinder
    {
        private readonly string _googleApiKey;
        private readonly HttpClient _httpClient;
        private readonly Random _random;

        public RouteFinder(string googleApiKey)
        {
            _googleApiKey = googleApiKey;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
            _random = new Random();
        }

        /// <summary>
        /// Основной метод поиска маршрутов
        /// </summary>
        public async Task<List<GMapRoute>> FindRoutesFromPointAsync(PointLatLng startPoint, int radiusMeters = 1000, int maxRoutes = 3)
        {
            var routes = new List<GMapRoute>();

            try
            {
                // 1. Получаем доступные дороги в радиусе
                var roadPoints = await GetNearbyRoadsAsync(startPoint, radiusMeters);

                if (roadPoints.Count == 0)
                {
                    // Если нет дорог, используем алгоритм поиска по направлениям
                    return await GenerateRoutesByDirectionAsync(startPoint, radiusMeters, maxRoutes);
                }

                // 2. Выбираем точки на дорогах для построения маршрутов
                var routePoints = SelectRoutePoints(startPoint, roadPoints, maxRoutes);

                // 3. Строим маршруты к выбранным точкам
                foreach (var targetPoint in routePoints)
                {
                    var route = await BuildRouteAsync(startPoint, targetPoint);
                    if (route != null && route.Points.Count > 1)
                    {
                        route.Name = $"Маршрут {routes.Count + 1}";
                        routes.Add(route);

                        if (routes.Count >= maxRoutes)
                            break;
                    }
                }

                // 4. Если маршрутов меньше нужного, добавляем дополнительные
                if (routes.Count < maxRoutes)
                {
                    var additionalRoutes = await GenerateAdditionalRoutesAsync(startPoint, radiusMeters, maxRoutes - routes.Count);
                    routes.AddRange(additionalRoutes);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в FindRoutesFromPointAsync: {ex.Message}");
                // Создаем простые маршруты как запасной вариант
                routes = CreateSimpleRoutes(startPoint, radiusMeters, maxRoutes);
            }

            return routes;
        }

        /// <summary>
        /// Получает точки на дорогах в заданном радиусе
        /// </summary>
        private async Task<List<PointLatLng>> GetNearbyRoadsAsync(PointLatLng centerPoint, int radiusMeters)
        {
            var roadPoints = new List<PointLatLng>();

            double latOffset = radiusMeters / 111120.0;
            double lngOffset = radiusMeters / (111120.0 * Math.Cos(centerPoint.Lat * Math.PI / 180));

            var south = centerPoint.Lat - latOffset;
            var north = centerPoint.Lat + latOffset;
            var west = centerPoint.Lng - lngOffset;
            var east = centerPoint.Lng + lngOffset;

            try
            {
                // Ищем дороги, тропы, пешеходные зоны
                string overpassQuery = $@"
                    [out:json];
                    (
                      way[""highway""]({south},{west},{north},{east});
                    );
                    out geom;";

                var url = $"http://overpass-api.de/api/interpreter?data={Uri.EscapeDataString(overpassQuery)}";
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                var elements = (JArray)json["elements"];

                foreach (var element in elements)
                {
                    var type = element["type"]?.ToString();
                    if (type != "way") continue;

                    var geometry = element["geometry"] as JArray;
                    if (geometry == null || geometry.Count == 0) continue;

                    // Добавляем начало, конец и середину дороги
                    if (geometry.Count >= 2)
                    {
                        // Начало дороги
                        var firstPoint = geometry.First();
                        roadPoints.Add(new PointLatLng(
                            (double)firstPoint["lat"],
                            (double)firstPoint["lon"]
                        ));

                        // Конец дороги
                        var lastPoint = geometry.Last();
                        roadPoints.Add(new PointLatLng(
                            (double)lastPoint["lat"],
                            (double)lastPoint["lon"]
                        ));

                        // Середина дороги
                        if (geometry.Count > 2)
                        {
                            var midIndex = geometry.Count / 2;
                            var midPoint = geometry[midIndex];
                            roadPoints.Add(new PointLatLng(
                                (double)midPoint["lat"],
                                (double)midPoint["lon"]
                            ));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при получении дорог: {ex.Message}");
            }

            // Удаляем дубликаты
            return roadPoints.Distinct().ToList();
        }

        /// <summary>
        /// Выбирает точки для маршрутов
        /// </summary>
        private List<PointLatLng> SelectRoutePoints(PointLatLng startPoint, List<PointLatLng> roadPoints, int count)
        {
            var selectedPoints = new List<PointLatLng>();

            // Сортируем по расстоянию и берем самые удаленные точки
            var sortedPoints = roadPoints
                .Select(p => new
                {
                    Point = p,
                    Distance = CalculateDistance(startPoint, p)
                })
                .Where(x => x.Distance > 50) // Минимум 50 метров
                .OrderByDescending(x => x.Distance) // Берем самые удаленные
                .Take(count * 2)
                .ToList();

            // Выбираем случайные точки из отобранных
            foreach (var point in sortedPoints.Take(count))
            {
                selectedPoints.Add(point.Point);
            }

            return selectedPoints;
        }

        /// <summary>
        /// Строит маршрут между двумя точками с использованием OSRM
        /// </summary>
        private async Task<GMapRoute> BuildRouteAsync(PointLatLng start, PointLatLng end)
        {
            try
            {
                // Используем OSRM (Open Source Routing Machine) для пешеходов
                var url = $"http://router.project-osrm.org/route/v1/foot/{start.Lng},{start.Lat};{end.Lng},{end.Lat}?" +
                         $"overview=full&geometries=geojson&steps=true";

                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);

                if (json["code"]?.ToString() != "Ok")
                {
                    // Если OSRM не нашел маршрут, пробуем Google
                    return await TryGoogleRouteAsync(start, end);
                }

                var route = json["routes"].First();
                var geometry = route["geometry"]?["coordinates"] as JArray;

                if (geometry == null)
                    return CreateBezierCurveRoute(start, end);

                var points = new List<PointLatLng>();
                foreach (var coord in geometry)
                {
                    // GeoJSON: [longitude, latitude]
                    var lon = (double)coord[0];
                    var lat = (double)coord[1];
                    points.Add(new PointLatLng(lat, lon));
                }

                return new GMapRoute(points, "Пеший маршрут");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в BuildRouteAsync: {ex.Message}");
                return CreateBezierCurveRoute(start, end);
            }
        }

        /// <summary>
        /// Пробует построить маршрут через Google Directions API
        /// </summary>
        private async Task<GMapRoute> TryGoogleRouteAsync(PointLatLng start, PointLatLng end)
        {
            if (string.IsNullOrEmpty(_googleApiKey) || _googleApiKey.Contains("AIzaSyBxe9rdMky1a04mz6RWYMf1ZFgSv15lzm4"))
                return CreateBezierCurveRoute(start, end);

            try
            {
                var url = $"https://maps.googleapis.com/maps/api/directions/json?" +
                         $"origin={start.Lat},{start.Lng}&" +
                         $"destination={end.Lat},{end.Lng}&" +
                         $"key={_googleApiKey}&" +
                         $"mode=walking";

                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);

                if (json["status"].ToString() != "OK")
                    return CreateBezierCurveRoute(start, end);

                var route = json["routes"].First();
                var polyline = route["overview_polyline"]?["points"]?.ToString();

                if (string.IsNullOrEmpty(polyline))
                    return CreateBezierCurveRoute(start, end);

                var points = DecodePolyline(polyline);
                return new GMapRoute(points, "Пеший маршрут (Google)");
            }
            catch
            {
                return CreateBezierCurveRoute(start, end);
            }
        }

        /// <summary>
        /// Генерирует маршруты по направлениям компаса
        /// </summary>
        private async Task<List<GMapRoute>> GenerateRoutesByDirectionAsync(PointLatLng startPoint, int radiusMeters, int count)
        {
            var routes = new List<GMapRoute>();

            var directions = new[]
            {
                (Name: "север", Angle: 0.0),
                (Name: "северо-восток", Angle: Math.PI/4),
                (Name: "восток", Angle: Math.PI/2),
                (Name: "юго-восток", Angle: 3*Math.PI/4),
                (Name: "юг", Angle: Math.PI),
                (Name: "юго-запад", Angle: 5*Math.PI/4),
                (Name: "запад", Angle: 3*Math.PI/2),
                (Name: "северо-запад", Angle: 7*Math.PI/4)
            };

            for (int i = 0; i < Math.Min(count, directions.Length); i++)
            {
                var direction = directions[i];
                double distance = radiusMeters * (0.6 + _random.NextDouble() * 0.4); // 60-100% радиуса

                double latOffset = (distance / 111120.0) * Math.Sin(direction.Angle);
                double lngOffset = (distance / (111120.0 * Math.Cos(startPoint.Lat * Math.PI / 180))) * Math.Cos(direction.Angle);

                var endPoint = new PointLatLng(
                    startPoint.Lat + latOffset,
                    startPoint.Lng + lngOffset
                );

                // Пытаемся найти ближайшую дорогу
                var nearestRoad = await FindNearestPointOnRoadAsync(endPoint, radiusMeters / 2);
                if (CalculateDistance(startPoint, nearestRoad) > 50)
                {
                    endPoint = nearestRoad;
                }

                var route = await BuildRouteAsync(startPoint, endPoint);
                if (route != null)
                {
                    route.Name = $"Направление: {direction.Name}";
                    routes.Add(route);
                }
            }

            return routes;
        }

        /// <summary>
        /// Находит ближайшую точку на дороге
        /// </summary>
        private async Task<PointLatLng> FindNearestPointOnRoadAsync(PointLatLng point, int searchRadius)
        {
            var roads = await GetNearbyRoadsAsync(point, searchRadius);
            if (roads.Count == 0) return point;

            return roads.OrderBy(r => CalculateDistance(point, r)).First();
        }

        /// <summary>
        /// Генерирует дополнительные маршруты
        /// </summary>
        private async Task<List<GMapRoute>> GenerateAdditionalRoutesAsync(PointLatLng startPoint, int radiusMeters, int count)
        {
            var routes = new List<GMapRoute>();

            try
            {
                // Ищем значимые POI
                var pois = await GetSignificantPOIAsync(startPoint, radiusMeters);

                foreach (var poi in pois.Take(count))
                {
                    var route = await BuildRouteAsync(startPoint, poi.Location);
                    if (route != null)
                    {
                        route.Name = $"К {poi.Name}";
                        routes.Add(route);
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки при поиске POI
            }

            return routes;
        }

        /// <summary>
        /// Получает значимые POI
        /// </summary>
        private async Task<List<POI>> GetSignificantPOIAsync(PointLatLng centerPoint, int radiusMeters)
        {
            var pois = new List<POI>();

            double latOffset = radiusMeters / 111120.0;
            double lngOffset = radiusMeters / (111120.0 * Math.Cos(centerPoint.Lat * Math.PI / 180));

            var south = centerPoint.Lat - latOffset;
            var north = centerPoint.Lat + latOffset;
            var west = centerPoint.Lng - lngOffset;
            var east = centerPoint.Lng + lngOffset;

            try
            {
                string overpassQuery = $@"
                    [out:json];
                    (
                      node[""amenity""~""hospital|police|fire_station|school|university|pharmacy""]({south},{west},{north},{east});
                      node[""shop""~""supermarket|convenience""]({south},{west},{north},{east});
                      node[""public_transport""~""stop_position|station""]({south},{west},{north},{east});
                    );
                    out;";

                var url = $"http://overpass-api.de/api/interpreter?data={Uri.EscapeDataString(overpassQuery)}";
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                var elements = (JArray)json["elements"];

                foreach (var element in elements)
                {
                    var type = element["type"]?.ToString();
                    var tags = element["tags"] as JObject;

                    if (tags == null || type != "node") continue;

                    var name = tags["name"]?.ToString() ?? "Объект";
                    var amenity = tags["amenity"]?.ToString() ??
                                 tags["shop"]?.ToString() ??
                                 tags["public_transport"]?.ToString() ??
                                 "unknown";

                    var location = new PointLatLng(
                        (double)element["lat"],
                        (double)element["lon"]
                    );

                    var distance = CalculateDistance(centerPoint, location);

                    pois.Add(new POI
                    {
                        Name = name,
                        Type = amenity,
                        Location = location,
                        Distance = distance
                    });
                }

                // Сортируем по расстоянию
                return pois.OrderBy(p => p.Distance).ToList();
            }
            catch
            {
                return pois;
            }
        }

        /// <summary>
        /// Создает простые маршруты как запасной вариант
        /// </summary>
        private List<GMapRoute> CreateSimpleRoutes(PointLatLng startPoint, int radiusMeters, int count)
        {
            var routes = new List<GMapRoute>();

            for (int i = 0; i < count; i++)
            {
                double angle = (2 * Math.PI / count) * i;
                double distance = radiusMeters * 0.7;

                double latOffset = (distance / 111120.0) * Math.Sin(angle);
                double lngOffset = (distance / (111120.0 * Math.Cos(startPoint.Lat * Math.PI / 180))) * Math.Cos(angle);

                var endPoint = new PointLatLng(
                    startPoint.Lat + latOffset,
                    startPoint.Lng + lngOffset
                );

                // Создаем кривую Безье для более естественного вида
                var route = CreateBezierCurveRoute(startPoint, endPoint);
                route.Name = $"Вариант {i + 1}";
                routes.Add(route);
            }

            return routes;
        }

        /// <summary>
        /// Создает маршрут в виде кривой Безье
        /// </summary>
        private GMapRoute CreateBezierCurveRoute(PointLatLng start, PointLatLng end)
        {
            var points = new List<PointLatLng> { start };

            // Создаем контрольные точки для кривой Безье
            double midLat = (start.Lat + end.Lat) / 2;
            double midLng = (start.Lng + end.Lng) / 2;

            // Добавляем случайное смещение для создания кривизны
            double offsetLat = (_random.NextDouble() - 0.5) * 0.001;
            double offsetLng = (_random.NextDouble() - 0.5) * 0.001;

            var controlPoint = new PointLatLng(midLat + offsetLat, midLng + offsetLng);

            // Генерируем точки кривой Безье
            for (double t = 0.1; t <= 1.0; t += 0.1)
            {
                double lat = Math.Pow(1 - t, 2) * start.Lat +
                            2 * (1 - t) * t * controlPoint.Lat +
                            Math.Pow(t, 2) * end.Lat;

                double lng = Math.Pow(1 - t, 2) * start.Lng +
                            2 * (1 - t) * t * controlPoint.Lng +
                            Math.Pow(t, 2) * end.Lng;

                points.Add(new PointLatLng(lat, lng));
            }

            points.Add(end);
            return new GMapRoute(points, "Предполагаемый маршрут");
        }

        /// <summary>
        /// Рассчитывает расстояние между двумя точками в метрах
        /// </summary>
        private double CalculateDistance(PointLatLng point1, PointLatLng point2)
        {
            double R = 6371000; // Радиус Земли в метрах
            double dLat = (point2.Lat - point1.Lat) * Math.PI / 180;
            double dLon = (point2.Lng - point1.Lng) * Math.PI / 180;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                      Math.Cos(point1.Lat * Math.PI / 180) * Math.Cos(point2.Lat * Math.PI / 180) *
                      Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        /// <summary>
        /// Декодирует полилайн Google Maps
        /// </summary>
        private List<PointLatLng> DecodePolyline(string encodedPoints)
        {
            var points = new List<PointLatLng>();
            int index = 0;
            int lat = 0, lng = 0;

            while (index < encodedPoints.Length)
            {
                int b, shift = 0, result = 0;
                do
                {
                    b = encodedPoints[index++] - 63;
                    result |= (b & 0x1f) << shift;
                    shift += 5;
                } while (b >= 0x20);

                int dlat = ((result & 1) != 0 ? ~(result >> 1) : (result >> 1));
                lat += dlat;

                shift = 0;
                result = 0;
                do
                {
                    b = encodedPoints[index++] - 63;
                    result |= (b & 0x1f) << shift;
                    shift += 5;
                } while (b >= 0x20);

                int dlng = ((result & 1) != 0 ? ~(result >> 1) : (result >> 1));
                lng += dlng;

                points.Add(new PointLatLng(lat / 1E5, lng / 1E5));
            }

            return points;
        }
    }
}