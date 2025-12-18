using GMap.NET;
using GMap.NET.WindowsForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

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
        private readonly HttpClient _httpClient;
        private readonly Random _random;
        public RouteFinder()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            _random = new Random();
        }
        /// <summary>
        /// Основной метод поиска маршрутов к ближайшим точкам интереса
        /// </summary>
        public async Task<List<GMapRoute>> FindRoutesFromPointAsync(PointLatLng startPoint, int radiusMeters = 1000, int maxRoutes = 3)
        {
            var routes = new List<GMapRoute>();

            Console.WriteLine($"=== FindRoutesFromPointAsync начат ===");
            Console.WriteLine($"Стартовая точка: {startPoint.Lat:F6}, {startPoint.Lng:F6}");

            try
            {
                // 1. Ищем ближайшие точки интереса
                var pois = await GetNearbyPOIAsync(startPoint, radiusMeters);

                Console.WriteLine($"Из GetNearbyPOIAsync получено POI: {pois.Count}");

                // 2. Сортируем по важности и расстоянию
                var sortedPOIs = pois
                    .OrderBy(p => GetPOIPriority(p.Type)) // Сначала более важные
                    .ThenBy(p => p.Distance)              // Потом ближайшие
                    .Take(maxRoutes)                     // Берем максимум маршрутов
                    .ToList();

                Console.WriteLine($"Отобрано для маршрутов: {sortedPOIs.Count}");

                // 3. Создаем прямые маршруты к точкам интереса
                foreach (var poi in sortedPOIs)
                {
                    var route = CreateDirectRoute(startPoint, poi.Location);
                    route.Name = $"К {poi.Name} ({poi.Type})";
                    routes.Add(route);
                    Console.WriteLine($"Создан маршрут к: {poi.Name} ({poi.Type}), расстояние: {poi.Distance:F0}м");
                }

                // 4. Если не нашли достаточно POI, добавляем направления
                if (routes.Count < maxRoutes)
                {
                    Console.WriteLine($"Недостаточно POI ({routes.Count}/{maxRoutes}). Добавляем направления...");
                    var additionalRoutes = CreateDirectionRoutes(startPoint, radiusMeters, maxRoutes - routes.Count);
                    routes.AddRange(additionalRoutes);

                    foreach (var route in additionalRoutes)
                    {
                        Console.WriteLine($"Добавлен резервный маршрут: {route.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в FindRoutesFromPointAsync: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");

                // Создаем простые маршруты в разные стороны
                routes = CreateSimpleDirectionRoutes(startPoint, radiusMeters, maxRoutes);
                Console.WriteLine("Использованы резервные маршруты");
            }

            Console.WriteLine($"=== FindRoutesFromPointAsync завершен. Всего маршрутов: {routes.Count} ===\n");
            return routes;
        }
        /// <summary>
        /// Ищет точки интереса поблизости
        /// </summary>
        private async Task<List<POI>> GetNearbyPOIAsync(PointLatLng center, int radiusMeters)
        {
            var pois = new List<POI>();

            radiusMeters = 500;

            double latOffset = radiusMeters / 111120.0;
            double lngOffset = radiusMeters / (111120.0 * Math.Cos(center.Lat * Math.PI / 180));

            var south = center.Lat - latOffset;
            var north = center.Lat + latOffset;
            var west = center.Lng - lngOffset;
            var east = center.Lng + lngOffset;

            Console.WriteLine($"=== GetNearbyPOIAsync начат ===");
            Console.WriteLine($"Центр: {center.Lat:F6}, {center.Lng:F6}");
            Console.WriteLine($"Область: {south:F6}, {west:F6}, {north:F6}, {east:F6}");

            try
            {
                string overpassQuery = $"[out:json];node[\"shop\"]({south.ToString(System.Globalization.CultureInfo.InvariantCulture)},{west.ToString(System.Globalization.CultureInfo.InvariantCulture)},{north.ToString(System.Globalization.CultureInfo.InvariantCulture)},{east.ToString(System.Globalization.CultureInfo.InvariantCulture)});out;";

                Console.WriteLine($"Запрос: {overpassQuery}");

                var url = $"https://maps.mail.ru/osm/tools/overpass/api/interpreter?data={Uri.EscapeDataString(overpassQuery)}";

                var response = await _httpClient.GetStringAsync(url);
                Console.WriteLine($"Получен ответ, длина: {response.Length} символов");

                // Проверяем начало ответа
                if (response.Length > 50)
                {
                    Console.WriteLine($"Начало ответа: {response.Substring(0, 50)}...");
                }

                var json = JObject.Parse(response);
                var elements = (JArray)json["elements"];

                Console.WriteLine($"Найдено элементов: {elements?.Count ?? 0}");

                if (elements != null && elements.Count > 0)
                {
                    foreach (var element in elements)
                    {
                        var type = element["type"]?.ToString();
                        if (type != "node") continue;

                        var tags = element["tags"] as JObject;
                        if (tags == null) continue;

                        string name = tags["name"]?.ToString() ?? "Без названия";
                        string shopType = tags["shop"]?.ToString() ?? "магазин";

                        var location = new PointLatLng(
                            (double)element["lat"],
                            (double)element["lon"]
                        );

                        var distance = CalculateDistance(center, location);

                        pois.Add(new POI
                        {
                            Name = name,
                            Type = $"Магазин ({shopType})",
                            Location = location,
                            Distance = Math.Round(distance)
                        });

                        Console.WriteLine($"  ✓ {name} - {shopType} ({distance:F0}м)");
                    }
                }
                else
                {
                    Console.WriteLine($"В этой области нет магазинов. Попробуйте кликнуть в центре города.");
                }
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"ОШИБКА HTTP: {httpEx.Message}");
                Console.WriteLine($"Сервер вернул ошибку 400 Bad Request.");
                Console.WriteLine($"Проблема в формате запроса или координатах.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ОБЩАЯ ОШИБКА: {ex.Message}");
            }

            Console.WriteLine($"=== GetNearbyPOIAsync завершен. Найдено POI: {pois.Count} ===\n");
            return pois;
        }
        /// <summary>
        /// Определяет приоритет точки интереса (чем меньше число, тем выше приоритет)
        /// </summary>
        private int GetPOIPriority(string poiType)
        {
            if (poiType.Contains("Больница") || poiType.Contains("Полиция"))
                return 1;
            if (poiType.Contains("Аптека"))
                return 2;
            if (poiType.Contains("Магазин"))
                return 3;
            if (poiType.Contains("остановка") || poiType.Contains("Автобус"))
                return 4;
            if (poiType.Contains("Кафе") || poiType.Contains("Ресторан") || poiType.Contains("Фастфуд"))
                return 5;
            if (poiType.Contains("Банк") || poiType.Contains("Банкомат"))
                return 6;

            return 7;
        }
        /// <summary>
        /// Создает прямой маршрут между двумя точками
        /// </summary>
        private GMapRoute CreateDirectRoute(PointLatLng start, PointLatLng end)
        {
            return new GMapRoute(new List<PointLatLng> { start, end }, "Прямой маршрут");
        }
        /// <summary>
        /// Создает маршруты в основные направления (север, восток, юг, запад)
        /// </summary>
        private List<GMapRoute> CreateDirectionRoutes(PointLatLng center, int radiusMeters, int count)
        {
            var routes = new List<GMapRoute>();
            var directions = new[]
            {
                ("север", 0.0),
                ("восток", Math.PI / 2),
                ("юг", Math.PI),
                ("запад", 3 * Math.PI / 2)
            };

            for (int i = 0; i < Math.Min(count, directions.Length); i++)
            {
                var (name, angle) = directions[i];
                var endPoint = CalculateDestination(center, angle, radiusMeters * 0.8);

                var route = CreateDirectRoute(center, endPoint);
                route.Name = $"Направление: {name}";
                routes.Add(route);
            }

            return routes;
        }
        /// <summary>
        /// Создает простые маршруты в разные стороны
        /// </summary>
        private List<GMapRoute> CreateSimpleDirectionRoutes(PointLatLng center, int radiusMeters, int count)
        {
            var routes = new List<GMapRoute>();

            for (int i = 0; i < count; i++)
            {
                double angle = (2 * Math.PI / count) * i;
                var endPoint = CalculateDestination(center, angle, radiusMeters * 0.7);

                var route = CreateDirectRoute(center, endPoint);
                route.Name = $"Направление {i + 1}";
                routes.Add(route);
            }

            return routes;
        }
        #region Вспомогательные геометрические методы
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
        /// Рассчитывает точку назначения по азимуту и расстоянию
        /// </summary>
        private PointLatLng CalculateDestination(PointLatLng start, double bearing, double distance)
        {
            double R = 6371000;
            double lat1 = start.Lat * Math.PI / 180;
            double lon1 = start.Lng * Math.PI / 180;

            double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(distance / R) +
                                   Math.Cos(lat1) * Math.Sin(distance / R) * Math.Cos(bearing));

            double lon2 = lon1 + Math.Atan2(Math.Sin(bearing) * Math.Sin(distance / R) * Math.Cos(lat1),
                                           Math.Cos(distance / R) - Math.Sin(lat1) * Math.Sin(lat2));

            return new PointLatLng(lat2 * 180 / Math.PI, lon2 * 180 / Math.PI);
        }
        #endregion
    }
}