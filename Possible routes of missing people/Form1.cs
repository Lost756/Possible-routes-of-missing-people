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

        public Form1()
        {
            InitializeComponent();
            SetupMap();
        }

        private void SetupMap()
        {
            gMapControl1 = new GMapControl();
            gMapControl1.Dock = DockStyle.Fill;
            this.Controls.Add(gMapControl1);

            gMapControl1.MapProvider = GMapProviders.OpenStreetMap;
            gMapControl1.Position = new PointLatLng(59.9391, 30.3158); // Санкт-Петербург
            gMapControl1.MinZoom = 0;
            gMapControl1.MaxZoom = 18;
            gMapControl1.Manager.Mode = AccessMode.ServerOnly;

            mainOverlay = new GMapOverlay("main");
            gMapControl1.Overlays.Add(mainOverlay);

            // Правильное событие для клика по карте
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

                // Определяем границы области 500x500 метров
                double latOffset = 0.00045; // ~50 метров (для теста)
                double lngOffset = 0.00060; // ~50 метров

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

                // Загружаем данные OSM для области
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
                MessageBox.Show($"Ошибка загрузки OSM: {ex.Message}");
            }
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // Form1
            // 
            this.ClientSize = new System.Drawing.Size(1035, 546);
            this.Name = "Form1";
            this.ResumeLayout(false);

        }
    }
}