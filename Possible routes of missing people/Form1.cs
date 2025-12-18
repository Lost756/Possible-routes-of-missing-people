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

                lastClickPoint = point;

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
                // Используем RouteFinder для получения возможных маршрутов
                var routes = await routeFinder.FindRoutesFromPointAsync(startPoint, 1000, 3);

                // Отображаем полученные маршруты на карте
                DisplayRoutesOnMap(routes);

                // Загружаем POI (точки интереса) в радиусе
                await LoadPOI(startPoint);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при поиске маршрутов: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DisplayRoutesOnMap(List<GMapRoute> routes)
        {
            foreach (var route in routes)
            {
                route.Stroke = new Pen(Color.Green, 3);
                route.Stroke.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                mainOverlay.Routes.Add(route);
            }
        }

        private async Task LoadPOI(PointLatLng centerPoint)
        {
            // Определяем границы области 1000x1000 метров (1 км)
            double latOffset = 1000.0 / 111120.0;
            double lngOffset = 1000.0 / (111120.0 * Math.Cos(centerPoint.Lat * Math.PI / 180));

            var topLeft = new PointLatLng(centerPoint.Lat + latOffset, centerPoint.Lng - lngOffset);
            var bottomRight = new PointLatLng(centerPoint.Lat - latOffset, centerPoint.Lng + lngOffset);

            // Загружаем POI через RouteFinder
            var pois = await routeFinder.GetNearbyPOIAsync(centerPoint, 1000);

            foreach (var poi in pois)
            {
                var marker = new GMarkerGoogle(poi.Location, GMarkerGoogleType.blue_small);
                marker.ToolTipText = $"{poi.Name} ({poi.Type})";
                mainOverlay.Markers.Add(marker);
            }
        }
    }
}