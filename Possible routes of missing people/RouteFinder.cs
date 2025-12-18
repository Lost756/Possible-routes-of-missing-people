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
    }

    public class RouteFinder
    {
        private readonly string _googleApiKey;
        private readonly HttpClient _httpClient;

        public RouteFinder(string googleApiKey)
        {
            _googleApiKey = googleApiKey;
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Находит возможные маршруты из заданной точки
        /// </summary>
        /// <param name="startPoint">Начальная точка</param>
        /// <param name="radiusMeters">Радиус поиска в метрах</param>
        /// <param name="maxRoutes">Максимальное количество маршрутов</param>
        /// <returns>Список возможных маршрутов</returns>
        public async Task<List<GMapRoute>> FindRoutesFromPointAsync(PointLatLng startPoint, int radiusMeters = 1000, int maxRoutes = 3)
        {
            var routes = new List<GMapRoute>();

            // Получаем ближайшие точки интереса (POI)
            var pois = await GetNearbyPOIAsync(startPoint, radiusMeters);

            // Фильтруем только значимые POI (дороги, здания, магазины и т.д.)
            var significantPOIs = pois
                .Where(p => IsSignificantPOI(p))
                .Take(maxRoutes)
                .ToList();

            // Для каждой значимой POI строим маршрут
            foreach (var poi in significantPOIs)
            {
                var route = await CalculateRouteAsync(startPoint, poi.Location);
                if (route != null)
                {
                    route.Name = $"Маршрут к {poi.Name}";
                    routes.Add(route);
                }
            }

            // Если не нашли достаточно маршрутов, добавляем прямые направления
            if (routes.Count < maxRoutes)
            {
                var additionalRoutes = GenerateRadialRoutes(startPoint, radiusMeters, maxRoutes - routes.Count);
                routes.AddRange(additionalRoutes);
            }

            return routes;
        }

        /// <summary>
        /// Получает ближайшие точки интереса (POI) через Overpass API
        /// </summary>
        public async Task<List<POI>> GetNearbyPOIAsync(PointLatLng centerPoint, int radiusMeters)
        {
            var pois = new List<POI>();

            double latOffset = radiusMeters / 111120.0;
            double lngOffset = radiusMeters / (111120.0 * Math.Cos(centerPoint.Lat * Math.PI / 180));

            var minLat = centerPoint.Lat - latOffset;
            var maxLat = centerPoint.Lat + latOffset;
            var minLon = centerPoint.Lng - lngOffset;
            var maxLon = centerPoint.Lng + lngOffset;

            try
            {
                string overpassQuery = $@"
                    [out:json];
                    (
                      node[""amenity""]({minLat},{minLon},{maxLat},{maxLon});
                      node[""shop""]({minLat},{minLon},{maxLat},{maxLon});
                      node[""tourism""]({minLat},{minLon},{maxLat},{maxLon});
                      way[""highway""]({minLat},{minLon},{maxLat},{maxLon});
                    );
                    out center;";

                var url = $"http://overpass-api.de/api/interpreter?data={Uri.EscapeDataString(overpassQuery)}";
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                var elements = (JArray)json["elements"];

                foreach (var element in elements)
                {
                    var type = element["type"]?.ToString();
                    var tags = element["tags"];

                    if (tags == null) continue;

                    var name = tags["name"]?.ToString() ?? "Без названия";
                    var amenity = tags["amenity"]?.ToString() ?? tags["shop"]?.ToString() ?? tags["tourism"]?.ToString() ?? "unknown";

                    PointLatLng location;

                    if (type == "node")
                    {
                        location = new PointLatLng((double)element["lat"], (double)element["lon"]);
                    }
                    else if (type == "way")
                    {
                        var center = element["center"];
                        location = new PointLatLng((double)center["lat"], (double)center["lon"]);
                    }
                    else
                    {
                        continue;
                    }

                    pois.Add(new POI
                    {
                        Name = name,
                        Type = amenity,
                        Location = location
                    });
                }
            }
            catch (Exception ex)
            {
                // Можно добавить логирование ошибок
                Console.WriteLine($"Ошибка при получении POI: {ex.Message}");
            }

            return pois;
        }

        /// <summary>
        /// Вычисляет маршрут между двумя точками
        /// </summary>
        private async Task<GMapRoute> CalculateRouteAsync(PointLatLng start, PointLatLng end)
        {
            try
            {
                // Используем Google Directions API
                var url = $"https://maps.googleapis.com/maps/api/directions/json?" +
                         $"origin={start.Lat},{start.Lng}&" +
                         $"destination={end.Lat},{end.Lng}&" +
                         $"key={_googleApiKey}&" +
                         $"mode=walking";

                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);

                if (json["status"].ToString() != "OK")
                    return null;

                var route = json["routes"].First();
                var polyline = route["overview_polyline"]["points"].ToString();

                // Декодируем полилайн в список точек
                var points = DecodePolyline(polyline);

                return new GMapRoute(points, "Маршрут");
            }
            catch
            {
                // В случае ошибки возвращаем простой прямой маршрут
                return new GMapRoute(new List<PointLatLng> { start, end }, "Прямой маршрут");
            }
        }

        /// <summary>
        /// Генерирует радиальные маршруты в разных направлениях
        /// </summary>
        private List<GMapRoute> GenerateRadialRoutes(PointLatLng center, int radiusMeters, int count)
        {
            var routes = new List<GMapRoute>();

            for (int i = 0; i < count; i++)
            {
                double angle = (2 * Math.PI / count) * i;

                double latOffset = (radiusMeters / 111120.0) * Math.Sin(angle);
                double lngOffset = (radiusMeters / (111120.0 * Math.Cos(center.Lat * Math.PI / 180))) * Math.Cos(angle);

                var endPoint = new PointLatLng(center.Lat + latOffset, center.Lng + lngOffset);

                var route = new GMapRoute(new List<PointLatLng> { center, endPoint }, $"Направление {i + 1}");
                routes.Add(route);
            }

            return routes;
        }

        /// <summary>
        /// Декодирует полилайн Google Maps в список точек
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

        /// <summary>
        /// Определяет, является ли POI значимым для построения маршрутов
        /// </summary>
        private bool IsSignificantPOI(POI poi)
        {
            var significantTypes = new[]
            {
                "highway", "road", "path", "building", "shop",
                "supermarket", "hospital", "police", "school"
            };

            // Приводим к нижнему регистру для сравнения без учета регистра
            var poiTypeLower = poi.Type?.ToLower() ?? "";
            var poiNameLower = poi.Name?.ToLower() ?? "";

            return significantTypes.Any(type =>
                poiTypeLower.Contains(type) || poiNameLower.Contains(type));
        }
    }
}