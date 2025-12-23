using GMap.NET;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Possible_routes_of_missing_people
{
    public class MapObject
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public PointLatLng Location { get; set; }
        public double Distance { get; set; }
        public string OSMId { get; set; }
    }

    public class RouteFinder
    {
        private readonly HttpClient _httpClient;

        // Определение типов объектов для поиска (только природа и дороги)
        private readonly Dictionary<string, string[]> _objectTypes = new Dictionary<string, string[]>
        {
            { "river", new[] { "river", "stream", "brook", "canal" } },
            { "water", new[] { "lake", "pond", "water", "reservoir" } },
            { "wetland", new[] { "wetland", "marsh", "swamp", "bog" } },
            { "meadow", new[] { "meadow", "grassland", "field", "farmland" } },
            { "forest", new[] { "forest", "wood" } },
            { "road", new[] { "path", "track", "footway", "road" } }
        };

        // Цвета маркеров для разных типов объектов
        private readonly Dictionary<string, (GMarkerGoogleType markerType, Color color)> _markerStyles = new Dictionary<string, (GMarkerGoogleType, Color)>
        {
            { "river", (GMarkerGoogleType.blue_dot, Color.Blue) },
            { "water", (GMarkerGoogleType.lightblue_dot, Color.LightBlue) },
            { "wetland", (GMarkerGoogleType.purple_dot, Color.Purple) },
            { "meadow", (GMarkerGoogleType.green_small, Color.Green) },
            { "forest", (GMarkerGoogleType.green_dot, Color.DarkGreen) },
            { "road", (GMarkerGoogleType.gray_small, Color.DarkGray) },
            { "highway", (GMarkerGoogleType.red_small, Color.DarkRed) } // Для основных дорог
        };

        public RouteFinder()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
        }

        /// <summary>
        /// Основной метод поиска объектов в указанном радиусе
        /// </summary>
        public async Task<GMapOverlay> FindObjectsInRadiusAsync(PointLatLng centerPoint, int radiusMeters = 1000, int maxObjectsPerType = 5)
        {
            var overlay = new GMapOverlay("nature_objects");

            Console.WriteLine($"=== Поиск природных объектов ===");
            Console.WriteLine($"Центр: {centerPoint.Lat:F6}, {centerPoint.Lng:F6}");
            Console.WriteLine($"Радиус поиска: {radiusMeters} м");

            try
            {
                // 1. Ищем объекты разных типов
                var allObjects = new List<MapObject>();

                // Поиск рек
                var rivers = await GetNearbyObjectsAsync(centerPoint, radiusMeters, "river");
                allObjects.AddRange(rivers);
                Console.WriteLine($"Найдено рек/ручьев: {rivers.Count}");

                // Поиск водоемов
                var waters = await GetNearbyObjectsAsync(centerPoint, radiusMeters, "water");
                allObjects.AddRange(waters);
                Console.WriteLine($"Найдено водоемов: {waters.Count}");

                // Поиск болот
                var wetlands = await GetNearbyObjectsAsync(centerPoint, radiusMeters, "wetland");
                allObjects.AddRange(wetlands);
                Console.WriteLine($"Найдено болот: {wetlands.Count}");

                // Поиск лугов и полей
                var meadows = await GetNearbyObjectsAsync(centerPoint, radiusMeters, "meadow");
                allObjects.AddRange(meadows);
                Console.WriteLine($"Найдено лугов/полей: {meadows.Count}");

                // Поиск лесов
                var forests = await GetNearbyObjectsAsync(centerPoint, radiusMeters, "forest");
                allObjects.AddRange(forests);
                Console.WriteLine($"Найдено лесов: {forests.Count}");

                // Поиск дорог и троп (пешеходные/лесные)
                var roads = await GetNearbyObjectsAsync(centerPoint, radiusMeters, "road");
                allObjects.AddRange(roads);
                Console.WriteLine($"Найдено дорог/троп: {roads.Count}");

                // Поиск основных дорог (шоссе, автострады)
                var highways = await GetNearbyHighwaysAsync(centerPoint, radiusMeters);
                allObjects.AddRange(highways);
                Console.WriteLine($"Найдено основных дорог: {highways.Count}");

                // 2. Удаляем дубликаты (объекты с одинаковыми OSMId)
                var uniqueObjects = allObjects
                    .GroupBy(o => o.OSMId)
                    .Select(g => g.First())
                    .ToList();

                Console.WriteLine($"Уникальных объектов всего: {uniqueObjects.Count}");

                // 3. Фильтруем по расстоянию и количеству
                var filteredObjects = uniqueObjects
                    .Where(o => o.Distance <= radiusMeters)
                    .GroupBy(o => o.Type)
                    .SelectMany(g => g.OrderBy(o => o.Distance).Take(maxObjectsPerType))
                    .ToList();

                Console.WriteLine($"Отобрано для отображения: {filteredObjects.Count}");

                // 4. Создаем маркеры на карте
                foreach (var obj in filteredObjects)
                {
                    var marker = CreateMarkerForObject(obj);
                    overlay.Markers.Add(marker);
                    Console.WriteLine($"Добавлен маркер: {obj.Type} - {obj.Name} ({obj.Distance:F0}м)");
                }

                // 5. Если объектов слишком мало, добавляем базовые маркеры
                if (filteredObjects.Count < 3)
                {
                    Console.WriteLine($"Мало объектов. Добавляем ориентиры...");
                    AddBasicOrientationMarkers(centerPoint, radiusMeters, overlay);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                AddBasicOrientationMarkers(centerPoint, radiusMeters, overlay);
            }

            Console.WriteLine($"=== Поиск завершен. Маркеров: {overlay.Markers.Count} ===\n");
            return overlay;
        }

        /// <summary>
        /// Ищет основные дороги (шоссе, автострады)
        /// </summary>
        private async Task<List<MapObject>> GetNearbyHighwaysAsync(PointLatLng center, int radiusMeters)
        {
            var highways = new List<MapObject>();

            Console.WriteLine($"Поиск: основные дороги");

            int searchRadius = Math.Min(radiusMeters, 3000);

            try
            {
                double latOffset = searchRadius / 111120.0;
                double lngOffset = searchRadius / (111120.0 * Math.Cos(center.Lat * Math.PI / 180));

                var south = center.Lat - latOffset;
                var north = center.Lat + latOffset;
                var west = center.Lng - lngOffset;
                var east = center.Lng + lngOffset;

                string bbox = $"{south.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                             $"{west.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                             $"{north.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                             $"{east.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

                // Запрос для основных дорог
                string overpassQuery = $"[out:json];" +
                                     $"(" +
                                     $"way[\"highway\"~\"motorway|trunk|primary|secondary|tertiary|unclassified|residential\"]({bbox});" +
                                     $");" +
                                     $"out center;";

                var server = "https://overpass-api.de/api/interpreter";

                try
                {
                    var url = $"{server}?data={Uri.EscapeDataString(overpassQuery)}";
                    var response = await _httpClient.GetStringAsync(url);

                    if (string.IsNullOrEmpty(response) || response.Contains("error"))
                    {
                        return highways;
                    }

                    var json = JObject.Parse(response);
                    var elements = (JArray)json["elements"];

                    if (elements != null)
                    {
                        foreach (var element in elements)
                        {
                            var highway = ParseHighwayElement(element, center);
                            if (highway != null)
                            {
                                highways.Add(highway);
                            }
                        }
                    }
                }
                catch
                {
                    return highways;
                }
            }
            catch
            {
                // Игнорируем ошибки
            }

            return highways;
        }

        /// <summary>
        /// Парсит элемент дороги
        /// </summary>
        private MapObject ParseHighwayElement(JToken element, PointLatLng center)
        {
            var type = element["type"]?.ToString();
            var id = element["id"]?.ToString();

            if (string.IsNullOrEmpty(id)) return null;

            double lat = 0, lon = 0;

            if (type == "node")
            {
                lat = (double?)element["lat"] ?? 0;
                lon = (double?)element["lon"] ?? 0;
            }
            else if (type == "way")
            {
                var centerData = element["center"];
                if (centerData != null)
                {
                    lat = (double?)centerData["lat"] ?? 0;
                    lon = (double?)centerData["lon"] ?? 0;
                }
            }

            if (lat == 0 || lon == 0) return null;

            var location = new PointLatLng(lat, lon);
            var distance = CalculateDistance(center, location);

            // Получаем информацию о дороге
            var tags = element["tags"] as JObject;
            string name = "Дорога";
            string roadType = "road";

            if (tags != null)
            {
                name = tags["name"]?.ToString() ??
                       tags["ref"]?.ToString() ??
                       GetHighwayTypeName(tags["highway"]?.ToString()) ??
                       "Дорога";

                roadType = "highway"; // Для стилизации основных дорог
            }

            // Сокращаем длинное название
            if (name.Length > 25)
                name = name.Substring(0, 22) + "...";

            return new MapObject
            {
                Name = name,
                Type = roadType,
                Location = location,
                Distance = Math.Round(distance),
                OSMId = $"highway_{id}"
            };
        }

        /// <summary>
        /// Преобразует тип дороги в читаемое название
        /// </summary>
        private string GetHighwayTypeName(string highwayType)
        {
            return highwayType switch
            {
                "motorway" => "Автострада",
                "trunk" => "Магистраль",
                "primary" => "Основная дорога",
                "secondary" => "Вторичная дорога",
                "tertiary" => "Третичная дорога",
                "unclassified" => "Дорога",
                "residential" => "Жилая улица",
                _ => "Дорога"
            };
        }

        /// <summary>
        /// Ищет объекты указанного типа поблизости
        /// </summary>
        private async Task<List<MapObject>> GetNearbyObjectsAsync(PointLatLng center, int radiusMeters, string objectType)
        {
            var objects = new List<MapObject>();

            Console.WriteLine($"Поиск: {objectType}");

            int searchRadius = Math.Min(radiusMeters, 3000);

            try
            {
                double latOffset = searchRadius / 111120.0;
                double lngOffset = searchRadius / (111120.0 * Math.Cos(center.Lat * Math.PI / 180));

                var south = center.Lat - latOffset;
                var north = center.Lat + latOffset;
                var west = center.Lng - lngOffset;
                var east = center.Lng + lngOffset;

                string overpassQuery = BuildOverpassQuery(south, west, north, east, objectType);

                if (string.IsNullOrEmpty(overpassQuery))
                {
                    return objects;
                }

                var server = "https://overpass-api.de/api/interpreter";

                try
                {
                    var url = $"{server}?data={Uri.EscapeDataString(overpassQuery)}";
                    var response = await _httpClient.GetStringAsync(url);

                    if (string.IsNullOrEmpty(response) || response.Contains("error"))
                    {
                        return objects;
                    }

                    var json = JObject.Parse(response);
                    var elements = (JArray)json["elements"];

                    if (elements != null)
                    {
                        foreach (var element in elements)
                        {
                            var obj = ParseOSMElement(element, center, objectType);
                            if (obj != null)
                            {
                                objects.Add(obj);
                            }
                        }
                    }
                }
                catch
                {
                    return objects;
                }
            }
            catch
            {
                // Игнорируем ошибки
            }

            return objects;
        }

        /// <summary>
        /// Строит Overpass API запрос
        /// </summary>
        private string BuildOverpassQuery(double south, double west, double north, double east, string objectType)
        {
            string bbox = $"{south.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                         $"{west.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                         $"{north.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                         $"{east.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

            return objectType switch
            {
                "river" => $"[out:json];" +
                          $"(" +
                          $"way[\"waterway\"~\"river|stream|brook|canal\"]({bbox});" +
                          $");" +
                          $"out center;",

                "water" => $"[out:json];" +
                          $"(" +
                          $"way[\"natural\"=\"water\"]({bbox});" +
                          $"way[\"water\"]({bbox});" +
                          $");" +
                          $"out center;",

                "wetland" => $"[out:json];" +
                            $"(" +
                            $"way[\"natural\"~\"wetland|marsh|swamp|bog\"]({bbox});" +
                            $");" +
                            $"out center;",

                "meadow" => $"[out:json];" +
                           $"(" +
                           $"way[\"natural\"=\"grassland\"]({bbox});" +
                           $"way[\"landuse\"~\"meadow|grass|farmland|field\"]({bbox});" +
                           $");" +
                           $"out center;",

                "forest" => $"[out:json];" +
                           $"(" +
                           $"way[\"natural\"~\"forest|wood\"]({bbox});" +
                           $"way[\"landuse\"=\"forest\"]({bbox});" +
                           $");" +
                           $"out center;",

                "road" => $"[out:json];" +
                         $"(" +
                         $"way[\"highway\"~\"path|track|footway|bridleway|cycleway\"]({bbox});" +
                         $");" +
                         $"out center;",

                _ => string.Empty
            };
        }

        /// <summary>
        /// Парсит элемент OSM
        /// </summary>
        private MapObject ParseOSMElement(JToken element, PointLatLng center, string objectType)
        {
            var type = element["type"]?.ToString();
            var id = element["id"]?.ToString();

            if (string.IsNullOrEmpty(id)) return null;

            double lat = 0, lon = 0;

            if (type == "node")
            {
                lat = (double?)element["lat"] ?? 0;
                lon = (double?)element["lon"] ?? 0;
            }
            else if (type == "way")
            {
                var centerData = element["center"];
                if (centerData != null)
                {
                    lat = (double?)centerData["lat"] ?? 0;
                    lon = (double?)centerData["lon"] ?? 0;
                }
            }

            if (lat == 0 || lon == 0) return null;

            var location = new PointLatLng(lat, lon);
            var distance = CalculateDistance(center, location);

            // Получаем название
            var tags = element["tags"] as JObject;
            string name = "Без названия";

            if (tags != null)
            {
                name = tags["name"]?.ToString() ??
                       tags["waterway"]?.ToString() ??
                       tags["natural"]?.ToString() ??
                       tags["landuse"]?.ToString() ??
                       tags["highway"]?.ToString() ??
                       "Без названия";
            }

            // Сокращаем длинное название
            if (name.Length > 25)
                name = name.Substring(0, 22) + "...";

            return new MapObject
            {
                Name = name,
                Type = objectType,
                Location = location,
                Distance = Math.Round(distance),
                OSMId = $"{type}_{id}"
            };
        }

        /// <summary>
        /// Создает маркер для объекта
        /// </summary>
        private GMarkerGoogle CreateMarkerForObject(MapObject mapObject)
        {
            // Получаем стиль маркера
            var (markerType, color) = _markerStyles.TryGetValue(mapObject.Type, out var style)
                ? style
                : (GMarkerGoogleType.yellow_small, Color.Yellow);

            var marker = new GMarkerGoogle(mapObject.Location, markerType);

            // Настраиваем всплывающую подсказку
            string typeName = mapObject.Type switch
            {
                "river" => "Река/Ручей",
                "water" => "Водоем",
                "wetland" => "Болото",
                "meadow" => "Луг/Поле",
                "forest" => "Лес",
                "road" => "Тропа/Дорожка",
                "highway" => "Дорога",
                _ => "Объект"
            };

            marker.ToolTipText = $"{typeName}\n" +
                               $"{mapObject.Name}\n" +
                               $"Расстояние: {mapObject.Distance:F0} м";

            marker.ToolTip.Fill = new SolidBrush(Color.FromArgb(240, Color.White));
            marker.ToolTip.Foreground = new SolidBrush(color);
            marker.ToolTip.Stroke = new Pen(color, 1);
            marker.ToolTip.TextPadding = new Size(8, 8);
            marker.ToolTip.Font = new Font("Arial", 8.5f);

            return marker;
        }

        /// <summary>
        /// Добавляет базовые маркеры ориентиров
        /// </summary>
        private void AddBasicOrientationMarkers(PointLatLng center, int radiusMeters, GMapOverlay overlay)
        {
            // Добавляем 4 маркера по сторонам света
            var directions = new[]
            {
                ("Север", 0.0, GMarkerGoogleType.arrow),
                ("Восток", Math.PI / 2, GMarkerGoogleType.arrow),
                ("Юг", Math.PI, GMarkerGoogleType.arrow),
                ("Запад", 3 * Math.PI / 2, GMarkerGoogleType.arrow)
            };

            foreach (var (direction, angle, markerType) in directions)
            {
                var location = CalculateDestination(center, angle, radiusMeters * 0.6);
                var marker = new GMarkerGoogle(location, markerType);
                marker.ToolTipText = $"Направление: {direction}";
                overlay.Markers.Add(marker);
            }
        }

        #region Вспомогательные геометрические методы

        /// <summary>
        /// Рассчитывает расстояние между двумя точками в метрах
        /// </summary>
        private double CalculateDistance(PointLatLng point1, PointLatLng point2)
        {
            double R = 6371000;
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