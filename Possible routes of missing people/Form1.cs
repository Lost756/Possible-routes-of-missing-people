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
        private SearchAreaCalculator areaCalculator;
        // Элементы управления
        private ComboBox cbAgeGroup;
        private Button btnApplySettings;
        private Label lblRadiusInfo;
        //Ключ Google API
        private const string GoogleApiKey = "AIzaSyBxe9rdMky1a04mz6RWYMf1ZFgSv15lzm4";
        public Form1()
        {
            InitializeComponent();
            InitializeControls();
            InitializeAreaCalculator();
            SetupMap();
            InitializeRouteFinder();
        }
        private void InitializeComponent()
        {
            this.Text = "Поиск возможных маршрутов пропавших людей";
            this.Size = new Size(1000, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.WhiteSmoke;
        }
        private void InitializeControls()
        {
            // Панель для элементов управления
            var controlPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                BackColor = Color.LightGray,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(10)
            };
            // Метка и выбор возраста
            var lblAge = new Label
            {
                Text = "Возрастная группа:",
                Location = new Point(10, 15),
                Size = new Size(130, 20),
                Font = new Font("Arial", 9)
            };
            cbAgeGroup = new ComboBox
            {
                Location = new Point(150, 12),
                Size = new Size(200, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Arial", 9)
            };
            cbAgeGroup.Items.AddRange(new[] { "Ребёнок", "Взрослый", "Пожилой человек" });
            cbAgeGroup.SelectedIndex = 1; // По умолчанию "Взрослый"
            cbAgeGroup.SelectedIndexChanged += OnSearchParametersChanged;

            // Кнопка применения настроек
            btnApplySettings = new Button
            {
                Text = "Применить",
                Location = new Point(370, 10),
                Size = new Size(100, 30),
                Font = new Font("Arial", 9, FontStyle.Bold),
                BackColor = Color.SteelBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnApplySettings.Click += OnApplySettingsClick;
            // Метка текущего радиуса
            lblRadiusInfo = new Label
            {
                Text = "Радиус поиска: 2000 м",
                Location = new Point(490, 15),
                Size = new Size(250, 20),
                Font = new Font("Arial", 9, FontStyle.Bold),
                ForeColor = Color.DarkRed
            };
            // Информационная метка
            var lblInfo = new Label
            {
                Text = "Кликните на карте в месте пропажи",
                Location = new Point(750, 15),
                Size = new Size(250, 20),
                Font = new Font("Arial", 9),
                ForeColor = Color.DarkBlue
            };
            // Добавляем элементы на панель
            controlPanel.Controls.AddRange(new Control[]
            {
                lblAge, cbAgeGroup, btnApplySettings, lblRadiusInfo, lblInfo
            });

            this.Controls.Add(controlPanel);
        }
        private void InitializeAreaCalculator()
        {
            areaCalculator = new SearchAreaCalculator();
            UpdateRadiusDisplay();
        }
        private void OnSearchParametersChanged(object sender, EventArgs e)
        {
            areaCalculator.SetParameters(cbAgeGroup.SelectedItem.ToString());
            UpdateRadiusDisplay();
        }
        private void OnApplySettingsClick(object sender, EventArgs e)
        {
            UpdateRadiusDisplay();

            MessageBox.Show($"Параметры поиска обновлены!\n\n" +
                          $"{areaCalculator.GetSearchInfo()}",
                          "Настройки",
                          MessageBoxButtons.OK,
                          MessageBoxIcon.Information);
        }
        private void UpdateRadiusDisplay()
        {
            lblRadiusInfo.Text = $"Радиус поиска: {areaCalculator.GetRadiusInMeters()} м";
        }
        private void InitializeRouteFinder()
        {
            routeFinder = new RouteFinder();
        }
        private void SetupMap()
        {
            gMapControl1 = new GMapControl();
            gMapControl1.Dock = DockStyle.Fill;
            gMapControl1.Top = 60; // Смещаем карту под панель управления
            this.Controls.Add(gMapControl1);
            // Настройка провайдера карт
            SetupMapProvider();

            gMapControl1.Position = new PointLatLng(61.6764, 50.8099); // Сыктывкар
            gMapControl1.MinZoom = 0;
            gMapControl1.MaxZoom = 18;
            gMapControl1.Manager.Mode = AccessMode.ServerAndCache;

            mainOverlay = new GMapOverlay("main");
            gMapControl1.Overlays.Add(mainOverlay);
            gMapControl1.MouseClick += OnMapMouseClick;
        }
        private void SetupMapProvider()
        {
            try
            {
                // Проверяем доступность Google Maps
                if (GMapProviders.GoogleMap != null &&
                    !string.IsNullOrEmpty(GoogleApiKey) &&
                    GoogleApiKey != "AIzaSyBxe9rdMky1a04mz6RWYMf1ZFgSv15lzm4")
                {
                    gMapControl1.MapProvider = GMapProviders.GoogleMap;
                    Console.WriteLine("Используется Google Maps");
                }
                else
                {
                    // Используем GoogleMap
                    gMapControl1.MapProvider = GMapProviders.GoogleMap;
                    Console.WriteLine("Используется GoogleMap");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при настройке провайдера карт: {ex.Message}");
                gMapControl1.MapProvider = GMapProviders.OpenStreetMap;
                Console.WriteLine("Используется OpenStreetMap (резервный вариант)");
            }
        }
        private async void OnMapMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Console.WriteLine("\n=== Клик на карте ===");

                PointLatLng point = gMapControl1.FromLocalToLatLng(e.X, e.Y);

                Console.WriteLine($"Координаты: {point.Lat:F6}, {point.Lng:F6}");
                Console.WriteLine(areaCalculator.GetSearchInfo());
                // Очищаем предыдущие элементы
                mainOverlay.Markers.Clear();
                mainOverlay.Polygons.Clear();
                mainOverlay.Routes.Clear();
                // Добавляем маркер места пропажи
                var marker = new GMarkerGoogle(point, GMarkerGoogleType.red);
                marker.ToolTipText = areaCalculator.GetMarkerTooltip(point);
                mainOverlay.Markers.Add(marker);
                // Рисуем область поиска
                DrawSearchArea(point);
                // Ищем маршруты
                await FindPossibleRoutes(point);
            }
        }
        private void DrawSearchArea(PointLatLng center)
        {
            // Получаем точки полигона из калькулятора
            var points = areaCalculator.CreateSearchAreaPolygon(center);
            // Создаем полигон с настройками из калькулятора
            var polygon = new GMapPolygon(points, "Область поиска")
            {
                Stroke = new Pen(areaCalculator.GetBorderColor(), areaCalculator.GetBorderWidth()),
                Fill = new SolidBrush(areaCalculator.GetAreaColor())
            };
            mainOverlay.Polygons.Add(polygon);
        }
        private async Task FindPossibleRoutes(PointLatLng startPoint)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                // Используем текущий радиус из калькулятора
                var routes = await routeFinder.FindRoutesFromPointAsync(
                    startPoint,
                    areaCalculator.GetRadiusInMeters(),
                    3);

                DisplayRoutesOnMap(routes);
                ZoomToSearchArea(startPoint);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при поиске маршрутов: {ex.Message}",
                              "Ошибка",
                              MessageBoxButtons.OK,
                              MessageBoxIcon.Error);
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
                              "Информация",
                              MessageBoxButtons.OK,
                              MessageBoxIcon.Information);
                return;
            }
            foreach (var route in routes)
            {
                var colors = new[] { Color.Green, Color.Blue, Color.Orange, Color.Purple, Color.DarkCyan };
                int colorIndex = mainOverlay.Routes.Count % colors.Length;

                route.Stroke = new Pen(colors[colorIndex], 3);
                route.Stroke.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                mainOverlay.Routes.Add(route);
            }
        }
        private void ZoomToSearchArea(PointLatLng center)
        {
            try
            {
                // Получаем прямоугольную область для зума из калькулятора
                var rect = areaCalculator.CalculateZoomRect(center);
                gMapControl1.SetZoomToFitRect(rect);
                gMapControl1.Position = center;
                // Устанавливаем рекомендуемый зум
                int recommendedZoom = areaCalculator.CalculateRecommendedZoom();
                if (gMapControl1.Zoom < recommendedZoom)
                    gMapControl1.Zoom = recommendedZoom;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при зуммировании: {ex.Message}");
                gMapControl1.Position = center;
                gMapControl1.Zoom = 14;
            }
        }
    }
}