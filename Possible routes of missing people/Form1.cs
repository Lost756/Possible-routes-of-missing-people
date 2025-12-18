using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;
using Newtonsoft.Json.Linq;

namespace Possible_routes_of_missing_people
{
    public partial class Form1 : Form
    {
        private GMapControl gMapControl1;
        private GMapOverlay mainOverlay;
        private RouteFinder routeFinder;

        //Ключ Google API
        private const string GoogleApiKey = "AIzaSyBxe9rdMky1a04mz6RWYMf1ZFgSv15lzm4";

        public Form1()
        {
            InitializeComponent();
            SetupMap();
            InitializeRouteFinder();
        }

        private void InitializeComponent()
        {
            this.Text = "Поиск возможных маршрутов (Google Maps)";
            this.Size = new Size(1000, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
        }

        private void InitializeRouteFinder()
        {
            routeFinder = new RouteFinder(GoogleApiKey);
        }

        private void SetupMap()
        {
            gMapControl1 = new GMapControl();
            gMapControl1.Dock = DockStyle.Fill;
            this.Controls.Add(gMapControl1);

            // Проверим, установлен ли API-ключ
            if (string.IsNullOrEmpty(GoogleApiKey) || GoogleApiKey == "AIzaSyBxe9rdMky1a04mz6RWYMf1ZFgSv15lzm4")
            {
                // Временно используем OSM или другой провайдер, если ключ не задан
                gMapControl1.MapProvider = GMapProviders.GoogleMap; // Заглушка
            }
            else
            {
                if (GMapProviders.GoogleMap != null)
                {
                    gMapControl1.MapProvider = GMapProviders.GoogleMap;
                }
                else
                {
                    MessageBox.Show("GMapProviders.GoogleMapProvider недоступен в текущей версии GMap.NET или не поддерживается.", "Предупреждение", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    // Используем OSM как запасной вариант
                    gMapControl1.MapProvider = GMapProviders.OpenStreetMap;
                }
            }

            gMapControl1.Position = new PointLatLng(61.6764, 50.8099); // Сыктывкар
            gMapControl1.MinZoom = 0;
            gMapControl1.MaxZoom = 18;

            // Режим: сначала с сервера, потом кэш
            gMapControl1.Manager.Mode = AccessMode.ServerAndCache;

            mainOverlay = new GMapOverlay("main");
            gMapControl1.Overlays.Add(mainOverlay);

            // Правильный вызов ZoomAndCenterMarkers
            gMapControl1.ZoomAndCenterMarkers(mainOverlay.Id);

            gMapControl1.MouseClick += OnMapMouseClick;
        }

        private async void OnMapMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // Преобразуем координаты клика в PointLatLng
                PointLatLng point = gMapControl1.FromLocalToLatLng(e.X, e.Y);

                // Очищаем предыдущие маркеры и полигоны
                mainOverlay.Markers.Clear();
                mainOverlay.Polygons.Clear();
                mainOverlay.Routes.Clear();

                // Ставим маркер в месте клика
                var marker = new GMarkerGoogle(point, GMarkerGoogleType.black_small);
                marker.ToolTipText = "Место пропажи";
                mainOverlay.Markers.Add(marker);

                // Вызываем метод поиска маршрутов
                await FindPossibleRoutes(point);
            }
        }

        private async Task FindPossibleRoutes(PointLatLng startPoint)
        {
            try
            {
                // Показываем прогресс
                Cursor = Cursors.WaitCursor;

                // Используем RouteFinder для получения возможных маршрутов
                var routes = await routeFinder.FindRoutesFromPointAsync(startPoint, 1000, 3);

                // Отображаем полученные маршруты на карте
                DisplayRoutesOnMap(routes);

                // Загружаем POI (точки интереса) в радиусе
                await LoadPOIAsync(startPoint);

                // Автоматически приближаем карту к области
                ZoomToArea(startPoint, 1000);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при поиске маршрутов: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void DisplayRoutesOnMap(List<GMapRoute> routes)
        {
            if (routes == null || routes.Count == 0)
            {
                MessageBox.Show("Не удалось построить маршруты. Возможно, в этом районе нет дорог.",
                    "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            foreach (var route in routes)
            {
                // Разные цвета для разных маршрутов
                var colors = new[] { Color.Green, Color.Blue, Color.Orange, Color.Purple, Color.Red };
                int colorIndex = mainOverlay.Routes.Count % colors.Length;

                route.Stroke = new Pen(colors[colorIndex], 3);
                route.Stroke.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                mainOverlay.Routes.Add(route);
            }
        }

        private async Task LoadPOIAsync(PointLatLng centerPoint)
        {
            try
            {
                // Определяем границы области 1000x1000 метров (1 км)
                double latOffset = 1000.0 / 111120.0;
                double lngOffset = 1000.0 / (111120.0 * Math.Cos(centerPoint.Lat * Math.PI / 180));

                var south = centerPoint.Lat - latOffset;
                var north = centerPoint.Lat + latOffset;
                var west = centerPoint.Lng - lngOffset;
                var east = centerPoint.Lng + lngOffset;

                string overpassQuery = $@"
                    [out:json];
                    (
                      node[""amenity""]({south},{west},{north},{east});
                      node[""shop""]({south},{west},{north},{east});
                      node[""tourism""]({south},{west},{north},{east});
                      node[""building""=""yes""]({south},{west},{north},{east});
                    );
                    out;";

                var url = $"http://overpass-api.de/api/interpreter?data={Uri.EscapeDataString(overpassQuery)}";

                // Используем HttpClient с таймаутом
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                var response = await client.GetStringAsync(url);
                var json = JObject.Parse(response);
                var elements = (JArray)json["elements"];

                int poiCount = 0;
                foreach (var element in elements)
                {
                    var type = element["type"]?.ToString();
                    var tags = element["tags"] as JObject;

                    if (tags == null || type != "node") continue;

                    var name = tags["name"]?.ToString() ?? "Без названия";
                    var amenity = tags["amenity"]?.ToString() ??
                                 tags["shop"]?.ToString() ??
                                 tags["tourism"]?.ToString() ??
                                 "Объект";

                    var lat = (double)element["lat"];
                    var lon = (double)element["lon"];
                    var point = new PointLatLng(lat, lon);

                    // Выбираем иконку в зависимости от типа POI
                    var markerType = GetMarkerTypeForPOI(amenity);
                    var marker = new GMarkerGoogle(point, markerType);
                    marker.ToolTipText = $"{name} ({amenity})";
                    mainOverlay.Markers.Add(marker);

                    poiCount++;

                    // Ограничиваем количество POI для производительности
                    if (poiCount >= 50) break;
                }

                Console.WriteLine($"Загружено {poiCount} POI");
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"Сетевая ошибка при загрузке POI: {httpEx.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки POI: {ex.Message}");
            }
        }

        private GMarkerGoogleType GetMarkerTypeForPOI(string poiType)
        {
            if (poiType.Contains("hospital") || poiType.Contains("clinic"))
                return GMarkerGoogleType.red_dot;
            if (poiType.Contains("shop") || poiType.Contains("market"))
                return GMarkerGoogleType.orange_dot;
            if (poiType.Contains("restaurant") || poiType.Contains("cafe"))
                return GMarkerGoogleType.yellow_dot;
            if (poiType.Contains("police") || poiType.Contains("fire"))
                return GMarkerGoogleType.blue_dot;
            if (poiType.Contains("school") || poiType.Contains("university"))
                return GMarkerGoogleType.purple_dot;

            return GMarkerGoogleType.green_dot;
        }

        private void ZoomToArea(PointLatLng center, double radiusMeters)
        {
            try
            {
                double latOffset = radiusMeters / 111120.0;
                double lngOffset = radiusMeters / (111120.0 * Math.Cos(center.Lat * Math.PI / 180));

                // Создаем прямоугольную область для зума
                var north = center.Lat + latOffset;
                var south = center.Lat - latOffset;
                var east = center.Lng + lngOffset;
                var west = center.Lng - lngOffset;

                // Создаем RectLatLng (северо-западный и юго-восточный углы)
                var rect = new RectLatLng(north, west, east - west, north - south);

                // Устанавливаем зум и центрируем карту
                gMapControl1.SetZoomToFitRect(rect);

                // Дополнительно центрируем на начальной точке
                gMapControl1.Position = center;

                // Устанавливаем подходящий зум
                if (gMapControl1.Zoom < 15)
                    gMapControl1.Zoom = 15;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при зуммировании: {ex.Message}");
                // Просто центрируем на точке
                gMapControl1.Position = center;
                gMapControl1.Zoom = 16;
            }
        }
    }
}