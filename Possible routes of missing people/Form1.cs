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
        private PointLatLng lastClickPoint;

        //Ключ Google API
        private const string GoogleApiKey = "AIzaSyBxe9rdMky1a04mz6RWYMf1ZFgSv15lzm4";
        public Form1()
        {
            InitializeComponent();
            SetupMap();
        }

        private void InitializeComponent()
        {
            this.Text = "Поиск возможных маршрутов (Google Maps)";
            this.Size = new Size(1000, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
        }

        private void SetupMap()
        {
            gMapControl1 = new GMapControl();
            gMapControl1.Dock = DockStyle.Fill;
            this.Controls.Add(gMapControl1);

            // Проверим, установлен ли API-ключ
            if (string.IsNullOrEmpty(GoogleApiKey) || GoogleApiKey == "AIzaSyBxe9rdMky1a04mz6RWYMf1ZFgSv15lzm4")
            {
                MessageBox.Show("Пожалуйста, установите действительный Google Maps API-ключ в коде.", "Ошибка API-ключа", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

                lastClickPoint = point;

                // Очищаем предыдущие маркеры и полигоны
                mainOverlay.Markers.Clear();
                mainOverlay.Polygons.Clear();
                mainOverlay.Routes.Clear();

                // Ставим маркер в месте клика
                var marker = new GMarkerGoogle(point, GMarkerGoogleType.black_small);
                marker.ToolTipText = "Место пропажи";
                mainOverlay.Markers.Add(marker);

                // Определяем границы области 1000x1000 метров (1 км)
                double latOffset = 1000.0 / 111120.0; // ~1 км в градусах широты
                // Долгота зависит от широты
                double lngOffset = 1000.0 / (111120.0 * Math.Cos(point.Lat * Math.PI / 180)); // ~1 км в градусах долготы

                var topLeft = new PointLatLng(point.Lat + latOffset, point.Lng - lngOffset);
                var topRight = new PointLatLng(point.Lat + latOffset, point.Lng + lngOffset);
                var bottomRight = new PointLatLng(point.Lat - latOffset, point.Lng + lngOffset);
                var bottomLeft = new PointLatLng(point.Lat - latOffset, point.Lng - lngOffset);

                var rectPoints = new List<PointLatLng>
                {
                    topLeft, topRight, bottomRight, bottomLeft, topLeft
                };

                var polygon = new GMapPolygon(rectPoints, "area");
                polygon.Stroke = new Pen(Color.Blue, 2);
                polygon.Fill = new SolidBrush(Color.FromArgb(50, Color.LightBlue));
                mainOverlay.Polygons.Add(polygon);

                // Загружаем данные OSM для области через Overpass API (для POI)
                await LoadOSMData(topLeft, bottomRight);
            }
        }

        private async Task LoadOSMData(PointLatLng topLeft, PointLatLng bottomRight)
        {
            try
            {
                var minLat = bottomRight.Lat;
                var maxLat = topLeft.Lat;
                var minLon = topLeft.Lng;
                var maxLon = bottomRight.Lng;

                string overpassQuery = $@"
                    [out:json];
                    (
                      node[""amenity""]({minLat},{minLon},{maxLat},{maxLon});
                      way[""highway""]({minLat},{minLon},{maxLat},{maxLon});
                      way[""building""]({minLat},{minLon},{maxLat},{maxLon});
                    );
                    out geom;";

                var url = $"http://overpass-api.de/api/interpreter?data={Uri.EscapeDataString(overpassQuery)}";
                using var client = new HttpClient();
                var response = await client.GetStringAsync(url);
                var json = JObject.Parse(response);
                var elements = (JArray)json["elements"];

                foreach (var element in elements)
                {
                    var type = element["type"]?.ToString();
                    var tags = element["tags"];
                    if (tags == null) continue;

                    var name = tags["name"]?.ToString() ?? "Без названия";
                    var amenity = tags["amenity"]?.ToString();
                    var highway = tags["highway"]?.ToString();
                    var building = tags["building"]?.ToString();

                    if (type == "node")
                    {
                        var lat = (double)element["lat"];
                        var lon = (double)element["lon"];
                        var point = new PointLatLng(lat, lon);

                        var marker = new GMarkerGoogle(point, GMarkerGoogleType.red_small);
                        marker.ToolTipText = $"{name} ({amenity})";
                        mainOverlay.Markers.Add(marker);
                    }
                    else if (type == "way")
                    {
                        var coordinates = element["geometry"]
                            .Select(x => new PointLatLng((double)x["lat"], (double)x["lon"]))
                            .ToList();

                        var route = new GMapRoute(coordinates, name);
                        route.Stroke = new Pen(Color.Red, 2);
                        mainOverlay.Routes.Add(route);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки OSM (для POI): {ex.Message}");
            }
        }
    }
}