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
        public string HighwayType { get; set; } // Новое поле для типа дороги
    }

    public class RouteFinder
    {
        private readonly HttpClient _httpClient;

        // Определение типов объектов для поиска
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
            { "highway", (GMarkerGoogleType.red_small, Color.DarkRed) },
            { "major_road", (GMarkerGoogleType.orange_small, Color.Orange) },
            { "minor_road", (GMarkerGoogleType.yellow_small, Color.Gold) }
        };

        // Приоритеты типов объектов (чем выше число, тем выше приоритет)
        private readonly Dictionary<string, int> _objectPriorities = new Dictionary<string, int>
        {
            { "highway", 10 },      // Основные дороги
            { "major_road", 9 },    // Важные дороги
            { "minor_road", 8 },    // Второстепенные дороги
            { "road", 7 },          // Тропы/дорожки
            { "river", 6 },         // Реки
            { "water", 5 },         // Водоемы
            { "forest", 4 },        // Леса
            { "meadow", 3 },        // Луга/поля
            { "wetland", 2 }        // Болота
        };

        public RouteFinder()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(25);
        }

        /// <summary>
        /// Основной метод поиска объектов в указанном радиусе
        /// </summary>
        public async Task<GMapOverlay> FindObjectsInRadiusAsync(PointLatLng centerPoint, int radiusMeters = 1000, int maxObjectsPerType = 10)
        {
            var overlay = new GMapOverlay("nature_objects");

            Console.WriteLine($"=== Поиск объектов ===");
            Console.WriteLine($"Центр: {centerPoint.Lat:F6}, {centerPoint.Lng:F6}");
            Console.WriteLine($"Радиус поиска: {radiusMeters} м");

            try
            {
                var allObjects = new List<MapObject>();

                // 1. Поиск ДОРОГ - самый важный для города
                Console.WriteLine("\n--- Поиск дорог ---");

                // Основные дороги (шоссе, магистрали)
                var highways = await GetNearbyHighwaysAsync(centerPoint, radiusMeters, "highway");
                allObjects.AddRange(highways);
                Console.WriteLine($"Найдено основных дорог: {highways.Count}");

                // Второстепенные дороги (городские)
                var cityRoads = await GetNearbyCityRoadsAsync(centerPoint, radiusMeters);
                allObjects.AddRange(cityRoads);
                Console.WriteLine($"Найдено городских дорог: {cityRoads.Count}");

                // Пешеходные дорожки и тропы
                var pedestrianRoads = await GetNearbyPedestrianRoadsAsync(centerPoint, radiusMeters);
                allObjects.AddRange(pedestrianRoads);
                Console.WriteLine($"Найдено пешеходных дорожек: {pedestrianRoads.Count}");

                // 2. Поиск природных объектов
                Console.WriteLine("\n--- Поиск природных объектов ---");

                // Реки и ручьи
                var rivers = await GetNearbyObjectsAsync(centerPoint, radiusMeters, "river");
                allObjects.AddRange(rivers);
                Console.WriteLine($"Найдено рек/ручьев: {rivers.Count}");

                // Водоемы
                var waters = await GetNearbyObjectsAsync(centerPoint, radiusMeters, "water");
                allObjects.AddRange(waters);
                Console.WriteLine($"Найдено водоемов: {waters.Count}");

                // Болота
                var wetlands = await GetNearbyObjectsAsync(centerPoint, radiusMeters, "wetland");
                allObjects.AddRange(wetlands);
                Console.WriteLine($"Найдено болот: {wetlands.Count}");

                // Луга и поля
                var meadows = await GetNearbyObjectsAsync(centerPoint, radiusMeters, "meadow");
                allObjects.AddRange(meadows);
                Console.WriteLine($"Найдено лугов/полей: {meadows.Count}");

                // Леса
                var forests = await GetNearbyObjectsAsync(centerPoint, radiusMeters, "forest");
                allObjects.AddRange(forests);
                Console.WriteLine($"Найдено лесов: {forests.Count}");

                Console.WriteLine($"\nВсего найдено объектов: {allObjects.Count}");

                // 3. Обработка и фильтрация объектов
                var filteredObjects = ProcessAndFilterObjects(allObjects, radiusMeters, maxObjectsPerType);

                // 4. Создание маркеров
                CreateMarkersForObjects(filteredObjects, overlay);

                // 5. Проверка результатов
                if (overlay.Markers.Count == 0)
                {
                    Console.WriteLine("Не найдено объектов. Добавляем базовые ориентиры...");
                    AddBasicOrientationMarkers(centerPoint, radiusMeters, overlay);
                }
                else if (overlay.Markers.Count < 5)
                {
                    Console.WriteLine($"Найдено мало объектов ({overlay.Markers.Count}). Добавляем дополнительные ориентиры...");
                    AddAdditionalOrientationMarkers(centerPoint, radiusMeters, overlay);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при поиске объектов: {ex.Message}");
                AddBasicOrientationMarkers(centerPoint, radiusMeters, overlay);
            }

            Console.WriteLine($"\n=== Поиск завершен. Маркеров: {overlay.Markers.Count} ===");
            return overlay;
        }

        /// <summary>
        /// Ищет основные дороги (шоссе, автострады, магистрали)
        /// </summary>
        private async Task<List<MapObject>> GetNearbyHighwaysAsync(PointLatLng center, int radiusMeters, string roadType)
        {
            var roads = new List<MapObject>();
            int searchRadius = CalculateAdjustedSearchRadius(radiusMeters, true);

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
                string overpassQuery = $"[out:json][timeout:30];" +
                                     $"(" +
                                     $"way[\"highway\"~\"motorway|trunk|primary|secondary\"]({bbox});" +
                                     $");" +
                                     $"out center geom;";

                Console.WriteLine($"Поиск основных дорог в области: {south:F6}, {west:F6} - {north:F6}, {east:F6}");

                var response = await ExecuteOverpassQueryAsync(overpassQuery);
                if (!string.IsNullOrEmpty(response))
                {
                    var json = JObject.Parse(response);
                    var elements = (JArray)json["elements"];

                    if (elements != null)
                    {
                        Console.WriteLine($"Найдено элементов дорог: {elements.Count}");

                        foreach (var element in elements)
                        {
                            var road = ParseRoadElement(element, center, "highway");
                            if (road != null && road.Distance <= radiusMeters * 1.5) // Увеличиваем допустимое расстояние
                            {
                                roads.Add(road);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при поиске основных дорог: {ex.Message}");
            }

            return roads;
        }

        /// <summary>
        /// Ищет городские дороги
        /// </summary>
        private async Task<List<MapObject>> GetNearbyCityRoadsAsync(PointLatLng center, int radiusMeters)
        {
            var roads = new List<MapObject>();
            int searchRadius = CalculateAdjustedSearchRadius(radiusMeters, true);

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

                // Запрос для городских дорог
                string overpassQuery = $"[out:json][timeout:30];" +
                                     $"(" +
                                     $"way[\"highway\"~\"tertiary|unclassified|residential|service\"]({bbox});" +
                                     $");" +
                                     $"out center geom;";

                var response = await ExecuteOverpassQueryAsync(overpassQuery);
                if (!string.IsNullOrEmpty(response))
                {
                    var json = JObject.Parse(response);
                    var elements = (JArray)json["elements"];

                    if (elements != null)
                    {
                        foreach (var element in elements)
                        {
                            var road = ParseRoadElement(element, center, "major_road");
                            if (road != null && road.Distance <= radiusMeters * 1.3)
                            {
                                roads.Add(road);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при поиске городских дорог: {ex.Message}");
            }

            return roads;
        }

        /// <summary>
        /// Ищет пешеходные дорожки и тропы
        /// </summary>
        private async Task<List<MapObject>> GetNearbyPedestrianRoadsAsync(PointLatLng center, int radiusMeters)
        {
            var roads = new List<MapObject>();
            int searchRadius = CalculateAdjustedSearchRadius(radiusMeters, false);

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

                // Запрос для пешеходных дорожек
                string overpassQuery = $"[out:json][timeout:25];" +
                                     $"(" +
                                     $"way[\"highway\"~\"footway|path|track|cycleway|pedestrian|steps\"]({bbox});" +
                                     $");" +
                                     $"out center geom;";

                var response = await ExecuteOverpassQueryAsync(overpassQuery);
                if (!string.IsNullOrEmpty(response))
                {
                    var json = JObject.Parse(response);
                    var elements = (JArray)json["elements"];

                    if (elements != null)
                    {
                        foreach (var element in elements)
                        {
                            var road = ParseRoadElement(element, center, "road");
                            if (road != null && road.Distance <= radiusMeters)
                            {
                                roads.Add(road);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при поиске пешеходных дорожек: {ex.Message}");
            }

            return roads;
        }

        /// <summary>
        /// Выполняет запрос к Overpass API
        /// </summary>
        private async Task<string> ExecuteOverpassQueryAsync(string query)
        {
            try
            {
                var server = "https://overpass-api.de/api/interpreter";
                var url = $"{server}?data={Uri.EscapeDataString(query)}";

                // Логируем короткую версию запроса для отладки
                Console.WriteLine($"Запрос: {query.Substring(0, Math.Min(100, query.Length))}...");

                var response = await _httpClient.GetStringAsync(url);

                if (string.IsNullOrEmpty(response) || response.Contains("error"))
                {
                    Console.WriteLine("Пустой ответ или ошибка от Overpass API");
                    return null;
                }

                return response;
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"HTTP ошибка при запросе: {httpEx.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при выполнении запроса: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Рассчитывает скорректированный радиус поиска
        /// </summary>
        private int CalculateAdjustedSearchRadius(int baseRadius, bool isForRoads)
        {
            if (isForRoads)
            {
                // Для дорог увеличиваем радиус поиска
                return Math.Min(baseRadius * 2, 5000);
            }
            else
            {
                // Для природных объектов используем обычный радиус
                return Math.Min(baseRadius, 3000);
            }
        }

        /// <summary>
        /// Парсит элемент дороги
        /// </summary>
        private MapObject ParseRoadElement(JToken element, PointLatLng center, string defaultType)
        {
            try
            {
                var type = element["type"]?.ToString();
                var id = element["id"]?.ToString();

                if (string.IsNullOrEmpty(id)) return null;

                double lat = 0, lon = 0;

                // Получаем координаты центра дороги
                var centerData = element["center"];
                if (centerData != null)
                {
                    lat = (double?)centerData["lat"] ?? 0;
                    lon = (double?)centerData["lon"] ?? 0;
                }
                else if (type == "node")
                {
                    lat = (double?)element["lat"] ?? 0;
                    lon = (double?)element["lon"] ?? 0;
                }
                else
                {
                    // Если нет центра, используем первую точку из геометрии
                    var geometry = element["geometry"] as JArray;
                    if (geometry != null && geometry.Count > 0)
                    {
                        lat = (double?)geometry[0]["lat"] ?? 0;
                        lon = (double?)geometry[0]["lon"] ?? 0;
                    }
                    else
                    {
                        return null;
                    }
                }

                if (lat == 0 || lon == 0) return null;

                var location = new PointLatLng(lat, lon);
                var distance = CalculateDistance(center, location);

                // Получаем информацию о дороге
                var tags = element["tags"] as JObject;
                string name = "Дорога";
                string roadType = defaultType;
                string highwayType = "road";

                if (tags != null)
                {
                    highwayType = tags["highway"]?.ToString() ?? "road";

                    name = tags["name"]?.ToString() ??
                           tags["ref"]?.ToString() ??
                           GetHighwayTypeName(highwayType) ??
                           "Дорога";

                    // Определяем тип для стилизации
                    if (highwayType == "motorway" || highwayType == "trunk" ||
                        highwayType == "primary" || highwayType == "secondary")
                    {
                        roadType = "highway";
                    }
                    else if (highwayType == "tertiary" || highwayType == "unclassified" ||
                             highwayType == "residential")
                    {
                        roadType = "major_road";
                    }
                    else
                    {
                        roadType = "minor_road";
                    }
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
                    OSMId = $"{type}_{id}",
                    HighwayType = highwayType
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка парсинга элемента дороги: {ex.Message}");
                return null;
            }
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
                "tertiary" => "Городская дорога",
                "unclassified" => "Дорога",
                "residential" => "Жилая улица",
                "service" => "Сервисная дорога",
                "footway" => "Пешеходная дорожка",
                "path" => "Тропа",
                "track" => "Грунтовая дорога",
                "cycleway" => "Велосипедная дорожка",
                "pedestrian" => "Пешеходная зона",
                "steps" => "Лестница",
                _ => "Дорога"
            };
        }

        /// <summary>
        /// Ищет объекты указанного типа поблизости
        /// </summary>
        private async Task<List<MapObject>> GetNearbyObjectsAsync(PointLatLng center, int radiusMeters, string objectType)
        {
            var objects = new List<MapObject>();
            int searchRadius = CalculateAdjustedSearchRadius(radiusMeters, false);

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

                var response = await ExecuteOverpassQueryAsync(overpassQuery);
                if (!string.IsNullOrEmpty(response))
                {
                    var json = JObject.Parse(response);
                    var elements = (JArray)json["elements"];

                    if (elements != null)
                    {
                        foreach (var element in elements)
                        {
                            var obj = ParseOSMElement(element, center, objectType);
                            if (obj != null && obj.Distance <= radiusMeters)
                            {
                                objects.Add(obj);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при поиске объектов типа {objectType}: {ex.Message}");
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
                "river" => $"[out:json][timeout:25];" +
                          $"(" +
                          $"way[\"waterway\"~\"river|stream|brook|canal\"]({bbox});" +
                          $");" +
                          $"out center geom;",

                "water" => $"[out:json][timeout:25];" +
                          $"(" +
                          $"way[\"natural\"=\"water\"]({bbox});" +
                          $"way[\"water\"]({bbox});" +
                          $");" +
                          $"out center geom;",

                "wetland" => $"[out:json][timeout:25];" +
                            $"(" +
                            $"way[\"natural\"~\"wetland|marsh|swamp|bog\"]({bbox});" +
                            $");" +
                            $"out center geom;",

                "meadow" => $"[out:json][timeout:25];" +
                           $"(" +
                           $"way[\"natural\"=\"grassland\"]({bbox});" +
                           $"way[\"landuse\"~\"meadow|grass|farmland|field\"]({bbox});" +
                           $");" +
                           $"out center geom;",

                "forest" => $"[out:json][timeout:25];" +
                           $"(" +
                           $"way[\"natural\"~\"forest|wood\"]({bbox});" +
                           $"way[\"landuse\"=\"forest\"]({bbox});" +
                           $");" +
                           $"out center geom;",

                _ => string.Empty
            };
        }

        /// <summary>
        /// Парсит элемент OSM
        /// </summary>
        private MapObject ParseOSMElement(JToken element, PointLatLng center, string objectType)
        {
            try
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
                    else
                    {
                        var geometry = element["geometry"] as JArray;
                        if (geometry != null && geometry.Count > 0)
                        {
                            // Берем среднюю точку
                            int middleIndex = geometry.Count / 2;
                            lat = (double?)geometry[middleIndex]["lat"] ?? 0;
                            lon = (double?)geometry[middleIndex]["lon"] ?? 0;
                        }
                    }
                }

                if (lat == 0 || lon == 0) return null;

                var location = new PointLatLng(lat, lon);
                var distance = CalculateDistance(center, location);

                // Получаем название
                var tags = element["tags"] as JObject;
                string name = GetDefaultNameForType(objectType);

                if (tags != null)
                {
                    name = tags["name"]?.ToString() ??
                           tags["waterway"]?.ToString() ??
                           tags["natural"]?.ToString() ??
                           tags["landuse"]?.ToString() ??
                           GetDefaultNameForType(objectType);
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
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка парсинга OSM элемента: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Возвращает название по умолчанию для типа объекта
        /// </summary>
        private string GetDefaultNameForType(string objectType)
        {
            return objectType switch
            {
                "river" => "Река/Ручей",
                "water" => "Водоем",
                "wetland" => "Болото",
                "meadow" => "Луг/Поле",
                "forest" => "Лес",
                "road" => "Тропа",
                _ => "Объект"
            };
        }

        /// <summary>
        /// Обрабатывает и фильтрует объекты
        /// </summary>
        private List<MapObject> ProcessAndFilterObjects(List<MapObject> allObjects, int radiusMeters, int maxObjectsPerType)
        {
            if (allObjects.Count == 0)
                return new List<MapObject>();

            Console.WriteLine($"\n--- Фильтрация объектов ---");
            Console.WriteLine($"Всего объектов до фильтрации: {allObjects.Count}");

            // 1. Удаляем явные дубликаты (одинаковые координаты и тип)
            var uniqueObjects = allObjects
                .GroupBy(o => new { o.Type, Lat = Math.Round(o.Location.Lat, 5), Lng = Math.Round(o.Location.Lng, 5) })
                .Select(g => g.OrderBy(o => o.Distance).First())
                .ToList();

            Console.WriteLine($"После удаления дубликатов: {uniqueObjects.Count}");

            // 2. Фильтруем по расстоянию с разными порогами для разных типов
            var filteredByDistance = uniqueObjects
                .Where(o =>
                {
                    var maxDistance = o.Type switch
                    {
                        "highway" => radiusMeters * 2.0,      // Основные дороги дальше
                        "major_road" => radiusMeters * 1.5,   // Городские дороги
                        "minor_road" => radiusMeters * 1.3,   // Второстепенные дороги
                        "road" => radiusMeters * 1.2,         // Пешеходные дорожки
                        _ => radiusMeters                     // Природные объекты
                    };
                    return o.Distance <= maxDistance;
                })
                .ToList();

            Console.WriteLine($"После фильтрации по расстоянию: {filteredByDistance.Count}");

            // 3. Группируем по типу и берем ограниченное количество каждого типа
            var groupedByType = filteredByDistance
                .GroupBy(o => o.Type)
                .OrderByDescending(g => GetPriority(g.Key));

            var result = new List<MapObject>();

            foreach (var group in groupedByType)
            {
                var objectsInGroup = group
                    .OrderBy(o => o.Distance)
                    .Take(maxObjectsPerType)
                    .ToList();

                result.AddRange(objectsInGroup);
                Console.WriteLine($"  {group.Key}: {objectsInGroup.Count} объектов");
            }

            Console.WriteLine($"Итого отобрано объектов: {result.Count}");
            return result;
        }

        /// <summary>
        /// Получает приоритет для типа объекта
        /// </summary>
        private int GetPriority(string objectType)
        {
            if (_objectPriorities.ContainsKey(objectType))
                return _objectPriorities[objectType];
            return 0;
        }

        /// <summary>
        /// Создает маркеры для объектов
        /// </summary>
        private void CreateMarkersForObjects(List<MapObject> objects, GMapOverlay overlay)
        {
            Console.WriteLine($"\n--- Создание маркеров ---");

            // Сортируем объекты по приоритету и расстоянию
            var sortedObjects = objects.OrderBy(o => GetPriority(o.Type)).ThenBy(o => o.Distance);

            foreach (var obj in sortedObjects)
            {
                try
                {
                    var marker = CreateMarkerForObject(obj);
                    overlay.Markers.Add(marker);
                    Console.WriteLine($"  Добавлен: {obj.Type} - '{obj.Name}' ({obj.Distance:F0} м)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Ошибка создания маркера для {obj.Name}: {ex.Message}");
                }
            }
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
                "highway" => "Основная дорога",
                "major_road" => "Городская дорога",
                "minor_road" => "Дорога",
                _ => "Объект"
            };

            string highwayInfo = !string.IsNullOrEmpty(mapObject.HighwayType) && mapObject.Type.Contains("road")
                ? $"\nТип: {GetHighwayTypeName(mapObject.HighwayType)}"
                : "";

            marker.ToolTipText = $"{typeName}{highwayInfo}\n" +
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

        /// <summary>
        /// Добавляет дополнительные ориентиры
        /// </summary>
        private void AddAdditionalOrientationMarkers(PointLatLng center, int radiusMeters, GMapOverlay overlay)
        {
            // Добавляем промежуточные направления
            var intermediateDirections = new[]
            {
                ("С-В", Math.PI / 4, GMarkerGoogleType.green_small),
                ("Ю-В", 3 * Math.PI / 4, GMarkerGoogleType.green_small),
                ("Ю-З", 5 * Math.PI / 4, GMarkerGoogleType.green_small),
                ("С-З", 7 * Math.PI / 4, GMarkerGoogleType.green_small)
            };

            foreach (var (direction, angle, markerType) in intermediateDirections)
            {
                var location = CalculateDestination(center, angle, radiusMeters * 0.4);
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