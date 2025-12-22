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
            Console.WriteLine($"Радиус поиска: {radiusMeters} м");

            try
            {
                // 1. Ищем ближайшие точки интереса (магазины)
                var pois = await GetNearbyShopsAsync(startPoint, radiusMeters);

                Console.WriteLine($"Найдено магазинов: {pois.Count}");

                // 2. Сортируем по расстоянию
                var sortedPOIs = pois
                    .OrderBy(p => p.Distance)
                    .Take(maxRoutes)
                    .ToList();

                Console.WriteLine($"Отобрано для маршрутов: {sortedPOIs.Count}");

                // 3. Создаем прямые маршруты к точкам интереса
                foreach (var poi in sortedPOIs)
                {
                    var route = CreateDirectRoute(startPoint, poi.Location);
                    route.Name = $"В магазин: {poi.Name}";
                    routes.Add(route);
                    Console.WriteLine($"Создан маршрут к: {poi.Name}, расстояние: {poi.Distance:F0}м");
                }

                // 4. Если не нашли достаточно магазинов, добавляем направления
                if (routes.Count < maxRoutes)
                {
                    Console.WriteLine($"Недостаточно магазинов ({routes.Count}/{maxRoutes}). Добавляем направления...");
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
        /// Ищет магазины поблизости (оригинальный рабочий запрос)
        /// </summary>
        private async Task<List<POI>> GetNearbyShopsAsync(PointLatLng center, int radiusMeters)
        {
            var pois = new List<POI>();

            Console.WriteLine($"=== GetNearbyShopsAsync начат ===");
            Console.WriteLine($"Центр: {center.Lat:F6}, {center.Lng:F6}");
            Console.WriteLine($"Запрошенный радиус: {radiusMeters} м");

            // Ограничиваем радиус для Overpass API (максимум 2 км для надежности)
            int searchRadius = Math.Min(radiusMeters, 2000);
            Console.WriteLine($"Фактический радиус поиска: {searchRadius} м");

            try
            {
                // Рассчитываем границы поиска
                double latOffset = searchRadius / 111120.0;
                double lngOffset = searchRadius / (111120.0 * Math.Cos(center.Lat * Math.PI / 180));

                var south = center.Lat - latOffset;
                var north = center.Lat + latOffset;
                var west = center.Lng - lngOffset;
                var east = center.Lng + lngOffset;

                Console.WriteLine($"Область поиска: {south:F6}, {west:F6}, {north:F6}, {east:F6}");

                string overpassQuery = $"[out:json];" +
                    $"node[\"shop\"]({south.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                    $"{west.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                    $"{north.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                    $"{east.ToString(System.Globalization.CultureInfo.InvariantCulture)});" +
                    $"out;";

                Console.WriteLine($"Запрос: {overpassQuery}");

                // Пробуем разные Overpass API серверы
                var servers = new[]
                {
                    "https://overpass-api.de/api/interpreter",  // Основной сервер
                    "https://maps.mail.ru/osm/tools/overpass/api/interpreter",  // Резервный
                    "https://lz4.overpass-api.de/api/interpreter"  // Быстрый сервер
                };

                foreach (var server in servers)
                {
                    try
                    {
                        Console.WriteLine($"Пробуем сервер: {server}");
                        var url = $"{server}?data={Uri.EscapeDataString(overpassQuery)}";

                        var response = await _httpClient.GetStringAsync(url);
                        Console.WriteLine($"Ответ получен, длина: {response.Length} символов");

                        if (string.IsNullOrEmpty(response) || response.Contains("error"))
                        {
                            Console.WriteLine($"Сервер {server} вернул ошибку или пустой ответ");
                            continue;
                        }

                        if (response.Length > 100)
                        {
                            Console.WriteLine($"Начало ответа: {response.Substring(0, Math.Min(100, response.Length))}...");
                        }

                        var json = JObject.Parse(response);
                        var elements = (JArray)json["elements"];

                        Console.WriteLine($"Найдено элементов: {elements?.Count ?? 0}");

                        if (elements != null && elements.Count > 0)
                        {
                            foreach (var element in elements)
                            {
                                try
                                {
                                    var type = element["type"]?.ToString();
                                    if (type != "node") continue;

                                    var tags = element["tags"] as JObject;
                                    if (tags == null) continue;

                                    string name = tags["name"]?.ToString() ?? "Магазин без названия";
                                    string shopType = tags["shop"]?.ToString() ?? "магазин";

                                    // Получаем координаты
                                    var lat = (double?)element["lat"] ?? 0;
                                    var lon = (double?)element["lon"] ?? 0;

                                    if (lat == 0 || lon == 0) continue;

                                    var location = new PointLatLng(lat, lon);
                                    var distance = CalculateDistance(center, location);

                                    // Фильтруем по реальному расстоянию
                                    if (distance <= searchRadius)
                                    {
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
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Ошибка обработки элемента: {ex.Message}");
                                    continue;
                                }
                            }

                            if (pois.Count > 0)
                            {
                                Console.WriteLine($"Успешно использован сервер: {server}");
                                break;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"На сервере {server} в этой области нет магазинов.");
                        }
                    }
                    catch (HttpRequestException)
                    {
                        Console.WriteLine($"Сервер {server} недоступен");
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка с сервером {server}: {ex.Message}");
                        continue;
                    }
                }

                if (pois.Count == 0)
                {
                    Console.WriteLine($"Все серверы вернули пустой результат или произошла ошибка.");
                    Console.WriteLine($"Попробуйте кликнуть в районе с магазинами (центр города).");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ОБЩАЯ ОШИБКА в GetNearbyShopsAsync: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
            }

            Console.WriteLine($"=== GetNearbyShopsAsync завершен. Найдено магазинов: {pois.Count} ===\n");
            return pois;
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