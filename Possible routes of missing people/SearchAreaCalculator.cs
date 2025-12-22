using System;
using System.Collections.Generic;
using System.Drawing;
using GMap.NET;

namespace Possible_routes_of_missing_people
{
    /// <summary>
    /// Класс для расчета и визуализации области поиска на основе возраста пропавшего
    /// </summary>
    public class SearchAreaCalculator
    {
        // Фиксированные радиусы для каждого возраста (в метрах)
        private const int CHILD_RADIUS_METERS = 1000;    // 1 км
        private const int ADULT_RADIUS_METERS = 2000;    // 2 км
        private const int ELDERLY_RADIUS_METERS = 1500;  // 1.5 км

        // Параметры поиска
        public string AgeGroup { get; private set; }
        public int CalculatedRadiusMeters { get; private set; }
        public double CalculatedRadiusKm { get; private set; }

        /// <summary>
        /// Инициализирует калькулятор с начальными параметрами
        /// </summary>
        public SearchAreaCalculator()
        {
            // Значения по умолчанию
            AgeGroup = "Взрослый";
            CalculateRadius();
        }
        /// <summary>
        /// Устанавливает параметры поиска и пересчитывает радиус
        /// </summary>
        public void SetParameters(string ageGroup)
        {
            AgeGroup = ageGroup;
            CalculateRadius();
        }
        /// <summary>
        /// Рассчитывает радиус поиска на основе выбранного возраста
        /// </summary>
        private void CalculateRadius()
        {
            // Определяем радиус в зависимости от возраста
            switch (AgeGroup)
            {
                case "Ребёнок":
                    CalculatedRadiusMeters = CHILD_RADIUS_METERS;
                    CalculatedRadiusKm = CHILD_RADIUS_METERS / 1000.0;
                    break;

                case "Пожилой человек":
                    CalculatedRadiusMeters = ELDERLY_RADIUS_METERS;
                    CalculatedRadiusKm = ELDERLY_RADIUS_METERS / 1000.0;
                    break;

                case "Взрослый":
                default:
                    CalculatedRadiusMeters = ADULT_RADIUS_METERS;
                    CalculatedRadiusKm = ADULT_RADIUS_METERS / 1000.0;
                    break;
            }
        }
        /// <summary>
        /// Возвращает информацию о текущих параметрах поиска
        /// </summary>
        public string GetSearchInfo()
        {
            return $"Возраст: {AgeGroup}\n" +
                   $"Радиус поиска: {CalculatedRadiusMeters} м ({CalculatedRadiusKm:F1} км)";
        }
        /// <summary>
        /// Возвращает текстовое описание для отображения в интерфейсе
        /// </summary>
        public string GetDisplayText()
        {
            return $"Текущий радиус поиска: {CalculatedRadiusMeters} м ({CalculatedRadiusKm:F1} км)";
        }
        /// <summary>
        /// Создает точки для отрисовки полигона области поиска (окружности)
        /// </summary>
        public List<PointLatLng> CreateSearchAreaPolygon(PointLatLng center, int segments = 36)
        {
            var points = new List<PointLatLng>();

            for (int i = 0; i < segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                var point = CalculateDestination(center, angle, CalculatedRadiusMeters);
                points.Add(point);
            }
            // Замыкаем полигон
            points.Add(points[0]);

            return points;
        }
        /// <summary>
        /// Создает точку назначения на заданном расстоянии и направлении от начальной точки
        /// </summary>
        private PointLatLng CalculateDestination(PointLatLng start, double bearing, double distance)
        {
            double R = 6371000; // Радиус Земли в метрах
            double lat1 = start.Lat * Math.PI / 180;
            double lon1 = start.Lng * Math.PI / 180;

            double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(distance / R) +
                                   Math.Cos(lat1) * Math.Sin(distance / R) * Math.Cos(bearing));

            double lon2 = lon1 + Math.Atan2(Math.Sin(bearing) * Math.Sin(distance / R) * Math.Cos(lat1),
                                           Math.Cos(distance / R) - Math.Sin(lat1) * Math.Sin(lat2));

            return new PointLatLng(lat2 * 180 / Math.PI, lon2 * 180 / Math.PI);
        }
        /// <summary>
        /// Рассчитывает границы прямоугольной области для зума карты
        /// </summary>
        public RectLatLng CalculateZoomRect(PointLatLng center)
        {
            // Добавляем отступ для лучшего отображения
            double paddedRadius = CalculatedRadiusMeters * 1.2;

            double latOffset = paddedRadius / 111120.0;
            double lngOffset = paddedRadius / (111120.0 * Math.Cos(center.Lat * Math.PI / 180));

            var north = center.Lat + latOffset;
            var south = center.Lat - latOffset;
            var east = center.Lng + lngOffset;
            var west = center.Lng - lngOffset;

            return new RectLatLng(north, west, east - west, north - south);
        }
        /// <summary>
        /// Рассчитывает рекомендуемый уровень зума для отображения области поиска
        /// </summary>
        public int CalculateRecommendedZoom()
        {
            // Эмпирическая формула для определения уровня зума на основе радиуса
            if (CalculatedRadiusKm > 3) return 13;
            if (CalculatedRadiusKm > 1.5) return 14;
            return 15; // Детальный зум для малых радиусов
        }
        /// <summary>
        /// Возвращает цвет области поиска в зависимости от радиуса
        /// </summary>
        public Color GetAreaColor()
        {
            // Меняем цвет в зависимости от возраста
            return AgeGroup switch
            {
                "Ребёнок" => Color.FromArgb(60, 255, 255, 100),   // Светло-зеленый для ребенка
                "Пожилой человек" => Color.FromArgb(70, 255, 200, 100), // Оранжевый для пожилого
                "Взрослый" => Color.FromArgb(80, 255, 100, 100),  // Красный для взрослого
                _ => Color.FromArgb(60, 50, 205, 50)              // По умолчанию зеленый
            };
        }
        /// <summary>
        /// Возвращает цвет границы области поиска
        /// </summary>
        public Color GetBorderColor()
        {
            return AgeGroup switch
            {
                "Ребёнок" => Color.FromArgb(200, 0, 180, 0),      // Зеленый для ребенка
                "Пожилой человек" => Color.FromArgb(200, 255, 140, 0), // Оранжевый для пожилого
                "Взрослый" => Color.FromArgb(200, 255, 0, 0),     // Красный для взрослого
                _ => Color.FromArgb(200, 0, 180, 0)               // По умолчанию зеленый
            };
        }
        /// <summary>
        /// Возвращает толщину границы в зависимости от радиуса
        /// </summary>
        public float GetBorderWidth()
        {
            return CalculatedRadiusKm switch
            {
                > 1.8f => 3.0f,  // Для взрослого (2 км)
                > 1.3f => 2.5f,  // Для пожилого (1.5 км)
                _ => 2.0f        // Для ребенка (1 км)
            };
        }
        /// <summary>
        /// Возвращает текст для всплывающей подсказки маркера
        /// </summary>
        public string GetMarkerTooltip(PointLatLng center)
        {
            return $"Место пропажи\n" +
                   $"{AgeGroup}\n" +
                   $"Радиус поиска: {CalculatedRadiusMeters} м\n" +
                   $"Координаты: {center.Lat:F5}, {center.Lng:F5}";
        }
        /// <summary>
        /// Возвращает радиус в километрах
        /// </summary>
        public double GetRadiusInKm()
        {
            return CalculatedRadiusKm;
        }
        /// <summary>
        /// Возвращает радиус в метрах
        /// </summary>
        public int GetRadiusInMeters()
        {
            return CalculatedRadiusMeters;
        }
    }
}