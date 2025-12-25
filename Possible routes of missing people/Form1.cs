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

                // Удаляем только старые объекты, но НЕ точку пропажи
                RemoveOldObjects();

                // Добавляем или обновляем маркер места пропажи
                UpdateDisappearanceMarker(point);

                // Рисуем область поиска
                DrawSearchArea(point);

                // Ищем и отображаем объекты
                await FindPossibleRoutes(point);
            }
        }

        /// <summary>
        /// Обновляет маркер места пропажи
        /// </summary>
        private void UpdateDisappearanceMarker(PointLatLng point)
        {
            // Удаляем старый маркер пропажи, если он есть
            var oldMarker = mainOverlay.Markers.FirstOrDefault(m => m.ToolTipText?.Contains("Место пропажи") == true);
            if (oldMarker != null)
            {
                mainOverlay.Markers.Remove(oldMarker);
            }

            // Добавляем новый маркер места пропажи
            var marker = new GMarkerGoogle(point, GMarkerGoogleType.red);
            marker.ToolTipText = areaCalculator.GetMarkerTooltip(point);
            marker.ToolTip.Fill = new SolidBrush(Color.FromArgb(220, Color.White));
            marker.ToolTip.Foreground = new SolidBrush(Color.DarkRed);
            marker.ToolTip.Stroke = new Pen(Color.DarkRed, 2);
            marker.ToolTip.Font = new Font("Arial", 9, FontStyle.Bold);

            mainOverlay.Markers.Add(marker);

            Console.WriteLine($"Добавлен маркер пропажи: {point.Lat:F5}, {point.Lng:F5}");
        }

        /// <summary>
        /// Удаляет старые объекты (но не точку пропажи)
        /// </summary>
        private void RemoveOldObjects()
        {
            // Находим и удаляем overlay с природными объектами
            var objectsOverlay = gMapControl1.Overlays.FirstOrDefault(o => o.Id == "nature_objects");
            if (objectsOverlay != null)
            {
                Console.WriteLine($"Удаляем старые объекты: {objectsOverlay.Markers.Count}");
                gMapControl1.Overlays.Remove(objectsOverlay);
            }

            // Удаляем старые полигоны областей поиска
            mainOverlay.Polygons.Clear();

            // Удаляем старые маршруты (если они есть)
            mainOverlay.Routes.Clear();

            gMapControl1.Refresh();
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

                // Получаем overlay с объектами
                var objectsOverlay = await routeFinder.FindObjectsInRadiusAsync(
                    startPoint,
                    areaCalculator.GetRadiusInMeters(),
                    7); // Увеличил количество объектов для города

                // Добавляем новый overlay с объектами
                if (objectsOverlay != null && objectsOverlay.Markers.Count > 0)
                {
                    gMapControl1.Overlays.Add(objectsOverlay);

                    // Обновляем отображение
                    gMapControl1.Refresh();

                    Console.WriteLine($"Найдено объектов: {objectsOverlay.Markers.Count}");

                    // Показываем информацию только если объектов мало
                    if (objectsOverlay.Markers.Count < 3)
                    {
                        MessageBox.Show($"Найдено объектов: {objectsOverlay.Markers.Count}\n" +
                                      "Попробуйте увеличить радиус поиска или выбрать другую точку.",
                                      "Результат поиска",
                                      MessageBoxButtons.OK,
                                      MessageBoxIcon.Information);
                    }
                }
                else
                {
                    MessageBox.Show("В указанном радиусе не найдено объектов.\n" +
                                  "Попробуйте:\n" +
                                  "1. Увеличить радиус поиска\n" +
                                  "2. Кликнуть в районе с дорогами или водоемами\n" +
                                  "3. Проверить интернет-соединение",
                                  "Информация",
                                  MessageBoxButtons.OK,
                                  MessageBoxIcon.Information);
                }

                ZoomToSearchArea(startPoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при поиске объектов: {ex.Message}");
                MessageBox.Show($"Ошибка при поиске объектов: {ex.Message}\n" +
                              "Проверьте интернет-соединение.",
                              "Ошибка",
                              MessageBoxButtons.OK,
                              MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
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