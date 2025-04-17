using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactions.Animated.Utilites;
using Avalonia.Xaml.Interactivity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Avalonia.Xaml.Interactions.Animated;

public class VerticalScrollViewerAnimatedBehavior : StyledElementBehavior<ScrollViewer>
{

    /// <summary>
    /// ScrollStepSize DirectProperty definition
    /// </summary>
    public static readonly DirectProperty<VerticalScrollViewerAnimatedBehavior, double> ScrollStepSizeProperty =
        AvaloniaProperty.RegisterDirect<VerticalScrollViewerAnimatedBehavior, double>(nameof(ScrollStepSize),
            o => o.ScrollStepSize,
            (o, v) => o.ScrollStepSize = v);

    private double _ScrollStepSize = 100;
    
    public double ScrollStepSize
    {
        get => _ScrollStepSize;
        set => SetAndRaise(ScrollStepSizeProperty, ref _ScrollStepSize, value);
    }



    public enum ChangeSize
    {
        Line,
        Page
    }

    public static readonly StyledProperty<ChangeSize> ScrollChangeSizeProperty = 
        AvaloniaProperty.Register<VerticalScrollViewerAnimatedBehavior, ChangeSize>(nameof(ScrollChangeSize));

    public ChangeSize ScrollChangeSize
    {
        get => GetValue(ScrollChangeSizeProperty);
        set => SetValue(ScrollChangeSizeProperty, value);
    }

    private const double AnimationDuration = 170; // animation duration in milliseconds

    private bool _isAnimating;
    private double _targetOffset;
    private double _startOffset;
    private DateTime _animationStartTime;

    private ScrollContentPresenter? scp;

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject!.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        AssociatedObject!.SetValue(ScrollChangeSizeProperty, ChangeSize.Line);

        AssociatedObject.Loaded += AssociatedObject_Loaded;
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        AssociatedObject!.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged);
    }

    private void AssociatedObject_Loaded(object? sender, RoutedEventArgs e)
    {
        if (AssociatedObject == null) return;

        scp = AssociatedObject?.Presenter as ScrollContentPresenter;

        AssociatedObject!.Loaded -= AssociatedObject_Loaded;
    }

    #region ScrollContentPresenter

    private static (double previous, double next) FindNearestSnapPoint(IReadOnlyList<double> snapPoints, double value)
    {
        var point = snapPoints.BinarySearch(value, Comparer<double>.Default);

        double previousSnapPoint, nextSnapPoint;

        if (point < 0)
        {
            point = ~point;

            previousSnapPoint = snapPoints[Math.Max(0, point - 1)];
            nextSnapPoint = point >= snapPoints.Count ? snapPoints.Last() : snapPoints[Math.Max(0, point)];
        }
        else
        {
            previousSnapPoint = nextSnapPoint = snapPoints[Math.Max(0, point)];
        }

        return (previousSnapPoint, nextSnapPoint);
    }

    private IScrollSnapPointsInfo? GetScrollSnapPointsInfo(object? content)
    {
        var scrollable = content;

        if (scp!.Content is ItemsControl itemsControl)
            scrollable = itemsControl.Presenter?.Panel;

        if (scp!.Content is ItemsPresenter itemsPresenter)
            scrollable = itemsPresenter.Panel;

        var snapPointsInfo = scrollable as IScrollSnapPointsInfo;

        return snapPointsInfo;
    }

    private Vector SnapOffset(Vector offset, Vector direction = default, bool snapToNext = false)
    {
        var scrollable = GetScrollSnapPointsInfo(scp!.Content);

        if (scrollable is null || scp!.VerticalSnapPointsType == SnapPointsType.None)
            return offset;

        var diff = GetAlignmentDiff();

        bool _areVerticalSnapPointsRegular = false;
        bool _areHorizontalSnapPointsRegular = false;
        IReadOnlyList<double>? _verticalSnapPoints = new List<double>();
        double _verticalSnapPoint = 0;
        double _verticalSnapPointOffset = 0;

        if (scrollable is IScrollSnapPointsInfo scrollSnapPointsInfo)
        {
            _areVerticalSnapPointsRegular = scrollSnapPointsInfo.AreVerticalSnapPointsRegular;
            _areHorizontalSnapPointsRegular = scrollSnapPointsInfo.AreHorizontalSnapPointsRegular;

            if (!_areVerticalSnapPointsRegular)
            {
                _verticalSnapPoints = scrollSnapPointsInfo.GetIrregularSnapPoints(Layout.Orientation.Vertical, scp!.VerticalSnapPointsAlignment);
            }
            else
            {
                _verticalSnapPoints = new List<double>();
                _verticalSnapPoint = scrollSnapPointsInfo.GetRegularSnapPoints(Layout.Orientation.Vertical, scp!.VerticalSnapPointsAlignment, out _verticalSnapPointOffset);
            }
        }

        if (scp!.VerticalSnapPointsType != SnapPointsType.None && (_areVerticalSnapPointsRegular || _verticalSnapPoints?.Count > 0) && (!snapToNext || snapToNext && direction.Y != 0))
        {
            var estimatedOffset = new Vector(offset.X, offset.Y + diff.Y);
            double previousSnapPoint = 0, nextSnapPoint = 0, midPoint = 0;

            if (_areVerticalSnapPointsRegular)
            {
                previousSnapPoint = (int)(estimatedOffset.Y / _verticalSnapPoint) * _verticalSnapPoint + _verticalSnapPointOffset;
                nextSnapPoint = previousSnapPoint + _verticalSnapPoint;
                midPoint = (previousSnapPoint + nextSnapPoint) / 2;
            }
            else if (_verticalSnapPoints?.Count > 0)
            {
                (previousSnapPoint, nextSnapPoint) = FindNearestSnapPoint(_verticalSnapPoints, estimatedOffset.Y);
                midPoint = (previousSnapPoint + nextSnapPoint) / 2;
            }

            var nearestSnapPoint = snapToNext 
                ? (direction.Y > 0 ? previousSnapPoint : nextSnapPoint) 
                : estimatedOffset.Y < midPoint ? previousSnapPoint : nextSnapPoint;

            offset = new Vector(offset.X, nearestSnapPoint - diff.Y);
        }

        Vector GetAlignmentDiff()
        {
            var vector = default(Vector);

            switch (scp!.VerticalSnapPointsAlignment)
            {
                case SnapPointsAlignment.Center:
                    vector += new Vector(0, scp!.Viewport.Height / 2);
                    break;
                case SnapPointsAlignment.Far:
                    vector += new Vector(0, scp!.Viewport.Height);
                    break;
            }

            switch (scp!.HorizontalSnapPointsAlignment)
            {
                case SnapPointsAlignment.Center:
                    vector += new Vector(scp!.Viewport.Width / 2, 0);
                    break;
                case SnapPointsAlignment.Far:
                    vector += new Vector(scp!.Viewport.Width, 0);
                    break;
            }

            return vector;
        }

        return offset;
    }

    #endregion

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!IsEnabled)
        {
            e.Handled = !scp?.IsScrollChainingEnabled ?? false;
            return;
        }

        if (scp == null)
        {
            scp = AssociatedObject?.Presenter as ScrollContentPresenter;
        }

        if (scp == null)
        {
            e.Handled = !scp?.IsScrollChainingEnabled ?? false;
            return;
        }

        var src = e.Source;
        while (src != null && src != scp)
        {
            if (src is ScrollContentPresenter scp2)
            {
                if (scp2 == scp)
                    break;

                if (e.Delta.Y > 0 && scp2.Offset.Y == 0 ||
                    e.Delta.Y < 0 && scp2.Offset.Y == scp2!.Extent.Height - scp2!.Viewport.Height) // scroll up or down & it's max
                {
                    src = scp2.GetVisualParent(); // take next parent
                }
                else
                {
                    return;
                }
            }

            if (src is Visual visual)
                src = visual.GetVisualParent();
        }

        if (src != scp)
        {
            e.Handled = !(src as ScrollContentPresenter)?.IsScrollChainingEnabled ?? false;
            //if (e.Handled)
                return;
        }

        var delta = e.Delta;
        var x = scp!.Offset.X;
        var y = scp!.Offset.Y;
        var maxOffsetY = scp!.Extent.Height - scp!.Viewport.Height;

        var scrollable = scp?.Child as ILogicalScrollable;
        var isLogical = scrollable?.IsLogicalScrollEnabled == true;
        if (scp!.Extent.Height > scp!.Viewport.Height)
        {
            double height = isLogical ? scrollable!.ScrollSize.Height : ScrollStepSize;
            y += -delta.Y * height;
            y = Math.Max(y, 0);
            y = Math.Min(y, maxOffsetY);
        }

        Vector newOffset = SnapOffset(new Vector(x, y), delta, true);
        var step = Math.Abs(newOffset.Y - scp!.Offset.Y);
        //var step = Math.Abs(y - scp!.Offset.Y);

        if (delta.Y > 0) // Scroll up
        {
            if (ScrollChangeSize == ChangeSize.Line)
            {
                AnimateScroll(-step);
            }
            else
            {
                AnimateScroll(-AssociatedObject!.Bounds.Height);
            }
        }
        else // Scroll down
        {
            if (ScrollChangeSize == ChangeSize.Line)
            {
                AnimateScroll(step);
            }
            else
            {
                AnimateScroll(AssociatedObject!.Bounds.Height);
            }
        }

        bool offsetChanged = newOffset != scp.Offset;
        e.Handled = !scp.IsScrollChainingEnabled || offsetChanged;
    }

    private void AnimateScroll(double delta)
    {
        var currentTime = DateTime.Now;

        if (_isAnimating)
        {
            // Calculate elapsed time and progress of the current animation
            var elapsedTime = (currentTime - _animationStartTime).TotalMilliseconds;
            double progress = Math.Min(elapsedTime / AnimationDuration, 1.0);

            // Use easing for the current progress
            var easing = new SineEaseOut();
            double easedProgress = easing.Ease(progress);

            // Update the current offset considering easing
            _startOffset += easedProgress * (_targetOffset - _startOffset);

            // Update _targetOffset with new delta
            _targetOffset += delta;
            _targetOffset = Math.Clamp(_targetOffset, 0, AssociatedObject!.Extent.Height - AssociatedObject!.Bounds.Height);

            _animationStartTime = currentTime;
        }
        else
        {
            _isAnimating = true;
            _startOffset = AssociatedObject!.Offset.Y;
            _targetOffset = _startOffset + delta;

            _targetOffset = Math.Clamp(_targetOffset, 0, AssociatedObject!.Extent.Height - AssociatedObject!.Bounds.Height);

            _animationStartTime = currentTime;
            _ = Animate();
        }
    }

    private async Task Animate()
    {
        while (_isAnimating)
        {
            var elapsedTime = (DateTime.Now - _animationStartTime).TotalMilliseconds;

            if (elapsedTime >= AnimationDuration)
            {
                // End the animation
                AssociatedObject!.Offset = new Vector(AssociatedObject!.Offset.X, _targetOffset);
                _isAnimating = false;
                break;
            }

            // Animation progress from 0 to 1
            double progress = elapsedTime / AnimationDuration;
            var easing = new SineEaseOut();
            double easedProgress = easing.Ease(progress);

            // Calculate new offset
            double currentOffset = _startOffset + (easedProgress * (_targetOffset - _startOffset));

            // Apply offset
            AssociatedObject!.Offset = new Vector(AssociatedObject!.Offset.X, currentOffset);

            await Task.Delay(10); // Update every 10ms
        }
    }
}