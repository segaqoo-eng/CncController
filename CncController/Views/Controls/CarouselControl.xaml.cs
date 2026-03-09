// [2026-03-06] CNC 斗笠式刀庫圓形刀盤視覺化控件（齒輪外形 + 刀具金屬感圖示 + 旋轉動畫）
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CncController.Models;

namespace CncController.Views.Controls
{
    public partial class CarouselControl : UserControl
    {
        #region Constants

        // [2026-03-06] 放大至 500x500（原 350x350）
        // [2026-03-06] 560x560 留白讓 P 標號不被邊緣切掉
        private const double CanvasSize = 560;
        private const double CenterX = CanvasSize / 2;
        private const double CenterY = CanvasSize / 2;
        private const double GearOuterRadius = 220;
        private const double GearInnerRadius = 185;
        private const double ToothDepth = 26;
        private const double ToolPlacementRadius = 150;
        private const double LabelRadius = GearOuterRadius + 20;

        #endregion

        #region DependencyProperties

        public static readonly DependencyProperty ToolCountProperty =
            DependencyProperty.Register(nameof(ToolCount), typeof(int), typeof(CarouselControl),
                new PropertyMetadata(12, OnLayoutPropertyChanged));

        public static readonly DependencyProperty CurrentAngleProperty =
            DependencyProperty.Register(nameof(CurrentAngle), typeof(double), typeof(CarouselControl),
                new PropertyMetadata(0.0, OnCurrentAngleChanged));

        public static readonly DependencyProperty CurrentPocketProperty =
            DependencyProperty.Register(nameof(CurrentPocket), typeof(int), typeof(CarouselControl),
                new PropertyMetadata(1, OnLayoutPropertyChanged));

        public static readonly DependencyProperty IsReferencedProperty =
            DependencyProperty.Register(nameof(IsReferenced), typeof(bool), typeof(CarouselControl),
                new PropertyMetadata(false, OnLayoutPropertyChanged));

        public static readonly DependencyProperty CenterTextProperty =
            DependencyProperty.Register(nameof(CenterText), typeof(string), typeof(CarouselControl),
                new PropertyMetadata(null, OnCenterTextChanged));

        public static readonly DependencyProperty SlotsProperty =
            DependencyProperty.Register(nameof(Slots), typeof(IEnumerable<AtcSlotInfo>), typeof(CarouselControl),
                new PropertyMetadata(null, OnLayoutPropertyChanged));

        public int ToolCount
        {
            get => (int)GetValue(ToolCountProperty);
            set => SetValue(ToolCountProperty, value);
        }

        public double CurrentAngle
        {
            get => (double)GetValue(CurrentAngleProperty);
            set => SetValue(CurrentAngleProperty, value);
        }

        public int CurrentPocket
        {
            get => (int)GetValue(CurrentPocketProperty);
            set => SetValue(CurrentPocketProperty, value);
        }

        public bool IsReferenced
        {
            get => (bool)GetValue(IsReferencedProperty);
            set => SetValue(IsReferencedProperty, value);
        }

        public string CenterText
        {
            get => (string)GetValue(CenterTextProperty);
            set => SetValue(CenterTextProperty, value);
        }

        public IEnumerable<AtcSlotInfo> Slots
        {
            get => (IEnumerable<AtcSlotInfo>)GetValue(SlotsProperty);
            set => SetValue(SlotsProperty, value);
        }

        #endregion

        #region Property Changed Callbacks

        private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CarouselControl ctrl && ctrl.IsLoaded)
                ctrl.RebuildVisuals();
        }

        private static void OnCurrentAngleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CarouselControl ctrl)
            {
                double newAngle = (double)e.NewValue;
                ctrl.AnimateToAngle(newAngle);
            }
        }

        private static void OnCenterTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CarouselControl ctrl)
                ctrl.UpdateCenterText();
        }

        #endregion

        private double _lastAnimatedAngle = 0;
        private bool _isFirstAngleSet = true; // [2026-03-09] 首次設定角度不動畫，直接跳轉

        public CarouselControl()
        {
            InitializeComponent();
            Loaded += (s, e) => RebuildVisuals();
        }

        #region Animation

        // [2026-03-06] 平滑旋轉動畫（EaseOut, 0.5s）
        private void AnimateToAngle(double targetAngle)
        {
            // [2026-03-09] 首次切換到 ATC 頁面時直接跳轉，不播放動畫
            if (_isFirstAngleSet)
            {
                _isFirstAngleSet = false;
                _lastAnimatedAngle = targetAngle;
                CarouselRotation.BeginAnimation(RotateTransform.AngleProperty, null);
                CarouselRotation.Angle = targetAngle;
                return;
            }

            var animation = new DoubleAnimation
            {
                From = _lastAnimatedAngle,
                To = targetAngle,
                Duration = TimeSpan.FromSeconds(0.5),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            animation.Completed += (s, e) => _lastAnimatedAngle = targetAngle;
            CarouselRotation.BeginAnimation(RotateTransform.AngleProperty, animation);
        }

        #endregion

        #region Center Text

        private void UpdateCenterText()
        {
            if (CenterTextBlock == null) return;

            if (!string.IsNullOrEmpty(CenterText))
            {
                CenterTextBlock.Text = CenterText;
            }
            else if (!IsReferenced)
            {
                CenterTextBlock.Text = "UN REFERENCED";
            }
            else
            {
                CenterTextBlock.Text = $"POCKET: {CurrentPocket}";
            }
        }

        #endregion

        #region Rebuild All Visuals

        // [2026-03-06] 重建所有視覺元素（齒輪 + 刀具 + 標號）
        private void RebuildVisuals()
        {
            MainCanvas.Children.Clear();

            int toolCount = Math.Max(4, ToolCount);
            var slotList = Slots?.ToList();

            // 1. 繪製齒輪外形
            DrawGear(toolCount);

            // 2. 繪製內圈填色
            DrawInnerCircle();

            // 3. 繪製每個刀位
            for (int i = 0; i < toolCount; i++)
            {
                double angleDeg = 360.0 / toolCount * i - 90; // 從 12 點鐘方向開始
                double angleRad = angleDeg * Math.PI / 180.0;

                int slotNumber = i + 1;
                bool hasTool = false;
                int toolNumber = 0;

                if (slotList != null && i < slotList.Count)
                {
                    hasTool = slotList[i].HasTool;
                    toolNumber = slotList[i].ToolNumber;
                }

                bool isCurrentPocket = (slotNumber == CurrentPocket);

                // 刀具或空位
                DrawToolSlot(angleDeg, angleRad, hasTool, toolNumber, isCurrentPocket);

                // 標號 P1~Pn
                DrawSlotLabel(angleDeg, angleRad, slotNumber);
            }

            UpdateCenterText();
        }

        #endregion

        #region Gear Path

        // [2026-03-06] 建構齒輪外形 PathGeometry（鋸齒凹槽）
        private void DrawGear(int toothCount)
        {
            var geometry = new PathGeometry();
            var figure = new PathFigure { IsClosed = true, IsFilled = true };

            double toothAngle = 360.0 / toothCount;
            // 每個齒分為 4 段：齒頂前斜面、齒頂、齒頂後斜面、齒谷
            double segAngle = toothAngle / 4.0;

            double outerR = GearOuterRadius;
            double innerR = GearOuterRadius - ToothDepth;

            // 起始點
            double startAngleRad = (-90.0 - toothAngle / 2.0) * Math.PI / 180.0;
            figure.StartPoint = new Point(
                CenterX + innerR * Math.Cos(startAngleRad),
                CenterY + innerR * Math.Sin(startAngleRad));

            for (int i = 0; i < toothCount; i++)
            {
                double baseAngle = -90.0 + toothAngle * i;

                // 齒谷 → 齒頂（上升斜面）
                double a1 = (baseAngle - toothAngle / 2.0 + segAngle) * Math.PI / 180.0;
                figure.Segments.Add(new LineSegment(
                    new Point(CenterX + outerR * Math.Cos(a1), CenterY + outerR * Math.Sin(a1)), true));

                // 齒頂弧（用兩段 LineSegment 近似短弧）
                double a2 = (baseAngle - toothAngle / 2.0 + segAngle * 2) * Math.PI / 180.0;
                figure.Segments.Add(new LineSegment(
                    new Point(CenterX + outerR * Math.Cos(a2), CenterY + outerR * Math.Sin(a2)), true));

                // 齒頂 → 齒谷（下降斜面）
                double a3 = (baseAngle - toothAngle / 2.0 + segAngle * 3) * Math.PI / 180.0;
                figure.Segments.Add(new LineSegment(
                    new Point(CenterX + innerR * Math.Cos(a3), CenterY + innerR * Math.Sin(a3)), true));

                // 齒谷弧
                double a4 = (baseAngle - toothAngle / 2.0 + segAngle * 4) * Math.PI / 180.0;
                figure.Segments.Add(new LineSegment(
                    new Point(CenterX + innerR * Math.Cos(a4), CenterY + innerR * Math.Sin(a4)), true));
            }

            geometry.Figures.Add(figure);

            var gearPath = new Path
            {
                Data = geometry,
                Stroke = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                StrokeThickness = 1.5,
                Fill = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))
            };
            MainCanvas.Children.Add(gearPath);

            // 齒輪外圈高光效果
            var outerGlow = new Ellipse
            {
                Width = GearOuterRadius * 2 + 4,
                Height = GearOuterRadius * 2 + 4,
                Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                StrokeThickness = 2,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(outerGlow, CenterX - GearOuterRadius - 2);
            Canvas.SetTop(outerGlow, CenterY - GearOuterRadius - 2);
            MainCanvas.Children.Add(outerGlow);
        }

        #endregion

        #region Inner Circle

        private void DrawInnerCircle()
        {
            // 內圈填色（深色中心）
            var innerBg = new Ellipse
            {
                Width = GearInnerRadius * 2 - 20,
                Height = GearInnerRadius * 2 - 20,
                Fill = new RadialGradientBrush(
                    Color.FromRgb(0x3A, 0x3A, 0x3A),
                    Color.FromRgb(0x25, 0x25, 0x25))
            };
            Canvas.SetLeft(innerBg, CenterX - GearInnerRadius + 10);
            Canvas.SetTop(innerBg, CenterY - GearInnerRadius + 10);
            MainCanvas.Children.Add(innerBg);

            // 內圈邊框
            var innerRing = new Ellipse
            {
                Width = GearInnerRadius * 2 - 20,
                Height = GearInnerRadius * 2 - 20,
                Stroke = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                StrokeThickness = 1.5,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(innerRing, CenterX - GearInnerRadius + 10);
            Canvas.SetTop(innerRing, CenterY - GearInnerRadius + 10);
            MainCanvas.Children.Add(innerRing);
        }

        #endregion

        #region Tool Slot Drawing

        // [2026-03-06] 繪製單一刀位（含高亮 / 空位虛線 / 刀具金屬感圖示）
        private void DrawToolSlot(double angleDeg, double angleRad, bool hasTool, int toolNumber, bool isCurrent)
        {
            double slotX = CenterX + ToolPlacementRadius * Math.Cos(angleRad);
            double slotY = CenterY + ToolPlacementRadius * Math.Sin(angleRad);

            if (hasTool)
            {
                // 繪製精緻刀具圖示
                var toolVisual = CreateToolVisual(toolNumber);

                // TransformGroup: 先旋轉刀具使刀刃朝圓心
                var transforms = new TransformGroup();
                // 刀具預設朝上繪製（刀刃在下=朝圓心），旋轉使其指向圓心
                transforms.Children.Add(new RotateTransform(angleDeg + 90, 0, 0));
                transforms.Children.Add(new TranslateTransform(slotX, slotY));
                toolVisual.RenderTransform = transforms;

                // 當前刀位高亮
                if (isCurrent)
                {
                    // [2026-03-06] 綠色發光圓（放大）
                    var glow = new Ellipse
                    {
                        Width = 52, Height = 52,
                        Fill = Brushes.Transparent,
                        Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x66)),
                        StrokeThickness = 3,
                        Effect = new DropShadowEffect
                        {
                            Color = Color.FromRgb(0x00, 0xFF, 0x66),
                            BlurRadius = 18,
                            ShadowDepth = 0,
                            Opacity = 0.9
                        }
                    };
                    Canvas.SetLeft(glow, slotX - 26);
                    Canvas.SetTop(glow, slotY - 26);
                    MainCanvas.Children.Add(glow);
                }

                MainCanvas.Children.Add(toolVisual);
            }
            else
            {
                // [2026-03-06] 空刀位：虛線圓（放大）
                var emptySlot = new Ellipse
                {
                    Width = 38, Height = 38,
                    Fill = Brushes.Transparent,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 3, 2 }
                };
                Canvas.SetLeft(emptySlot, slotX - 19);
                Canvas.SetTop(emptySlot, slotY - 19);
                MainCanvas.Children.Add(emptySlot);

                // 當前刀位即使空也要高亮
                if (isCurrent)
                {
                    var glow = new Ellipse
                    {
                        Width = 46, Height = 46,
                        Fill = Brushes.Transparent,
                        Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x66)),
                        StrokeThickness = 2.5,
                        Effect = new DropShadowEffect
                        {
                            Color = Color.FromRgb(0x00, 0xFF, 0x66),
                            BlurRadius = 15,
                            ShadowDepth = 0,
                            Opacity = 0.8
                        }
                    };
                    Canvas.SetLeft(glow, slotX - 23);
                    Canvas.SetTop(glow, slotY - 23);
                    MainCanvas.Children.Add(glow);
                }
            }
        }

        #endregion

        #region Tool Visual (Metallic)

        // [2026-03-06] 建構精緻金屬感刀具圖示（刀柄 + 夾持環 + 刀刃）
        // 刀具以原點 (0,0) 為中心繪製，刀刃朝下（-Y 方向 = 朝圓心）
        private Canvas CreateToolVisual(int toolNumber)
        {
            var toolCanvas = new Canvas { Width = 0, Height = 0 };

            // [2026-03-06] 刀柄（圓角矩形，金屬漸層）--- 放大 1.5x
            double shankW = 15, shankH = 30;
            var shankBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5)
            };
            shankBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x55, 0x55, 0x55), 0.0));
            shankBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xBB, 0xBB, 0xBB), 0.35));
            shankBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xDD, 0xDD, 0xDD), 0.5));
            shankBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xBB, 0xBB, 0xBB), 0.65));
            shankBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x55, 0x55, 0x55), 1.0));

            var shank = new Rectangle
            {
                Width = shankW,
                Height = shankH,
                RadiusX = 2,
                RadiusY = 2,
                Fill = shankBrush,
                Stroke = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                StrokeThickness = 0.6
            };
            Canvas.SetLeft(shank, -shankW / 2);
            Canvas.SetTop(shank, -shankH + 2); // 刀柄在上（朝外）
            toolCanvas.Children.Add(shank);

            // [2026-03-06] 夾持環（窄矩形，深色漸層）--- 放大 1.5x
            double clampW = 20, clampH = 6;
            var clampBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            clampBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x33, 0x33, 0x33), 0.0));
            clampBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x66, 0x66, 0x66), 0.5));
            clampBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x33, 0x33, 0x33), 1.0));

            var clamp = new Rectangle
            {
                Width = clampW,
                Height = clampH,
                Fill = clampBrush,
                Stroke = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
                StrokeThickness = 0.5
            };
            Canvas.SetLeft(clamp, -clampW / 2);
            Canvas.SetTop(clamp, 0); // 夾持環在中間過渡區
            toolCanvas.Children.Add(clamp);

            // [2026-03-06] 刀刃（梯形 Path，亮灰漸層）--- 放大 1.5x
            double bladeTopW = 15, bladeBottomW = 7, bladeH = 18;
            var bladeFigure = new PathFigure
            {
                StartPoint = new Point(-bladeTopW / 2, clampH),
                IsClosed = true,
                IsFilled = true
            };
            bladeFigure.Segments.Add(new LineSegment(new Point(bladeTopW / 2, clampH), true));
            bladeFigure.Segments.Add(new LineSegment(new Point(bladeBottomW / 2, clampH + bladeH), true));
            bladeFigure.Segments.Add(new LineSegment(new Point(-bladeBottomW / 2, clampH + bladeH), true));

            var bladeGeometry = new PathGeometry();
            bladeGeometry.Figures.Add(bladeFigure);

            var bladeBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5)
            };
            bladeBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x88, 0x88, 0x88), 0.0));
            bladeBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xCC, 0xCC, 0xCC), 0.4));
            bladeBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xEE, 0xEE, 0xEE), 0.55));
            bladeBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xCC, 0xCC, 0xCC), 0.7));
            bladeBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x88, 0x88, 0x88), 1.0));

            var blade = new Path
            {
                Data = bladeGeometry,
                Fill = bladeBrush,
                Stroke = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                StrokeThickness = 0.5
            };
            toolCanvas.Children.Add(blade);

            // [2026-03-06] 刀刃尖端亮點（放大）
            var tip = new Ellipse
            {
                Width = 4, Height = 4,
                Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))
            };
            Canvas.SetLeft(tip, -2);
            Canvas.SetTop(tip, clampH + bladeH - 3);
            toolCanvas.Children.Add(tip);

            // --- 刀具號文字（小字顯示在刀柄上）---
            if (toolNumber > 0)
            {
                var toolLabel = new TextBlock
                {
                    Text = $"T{toolNumber}",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0x88)),
                    TextAlignment = TextAlignment.Center
                };
                toolLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(toolLabel, -toolLabel.DesiredSize.Width / 2);
                Canvas.SetTop(toolLabel, -shankH + 7);
                toolCanvas.Children.Add(toolLabel);
            }

            return toolCanvas;
        }

        #endregion

        #region Slot Label

        // [2026-03-06] 刀位標號 P1~Pn（圓周外側）
        private void DrawSlotLabel(double angleDeg, double angleRad, int slotNumber)
        {
            double labelX = CenterX + LabelRadius * Math.Cos(angleRad);
            double labelY = CenterY + LabelRadius * Math.Sin(angleRad);

            var label = new TextBlock
            {
                Text = $"P{slotNumber}",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))
            };

            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, labelX - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, labelY - label.DesiredSize.Height / 2);

            // 反轉旋轉，使標號始終正向（不隨刀盤轉動後倒著）
            // 注意：標號隨 MainCanvas 旋轉，需反轉回來
            label.RenderTransformOrigin = new Point(0.5, 0.5);
            label.RenderTransform = new RotateTransform(-CurrentAngle);

            MainCanvas.Children.Add(label);
        }

        #endregion
    }
}
