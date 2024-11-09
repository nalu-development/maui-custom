using System;
using System.Diagnostics.CodeAnalysis;
using CoreAnimation;
using CoreGraphics;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using UIKit;
using static Microsoft.Maui.Primitives.Dimension;

namespace Microsoft.Maui.Platform
{
	public partial class WrapperView : UIView, IDisposable, IUIViewLifeCycleEvents, IMauiPlatformView
	{
		bool _invalidateParentWhenMovedToWindow;
		WeakReference<IViewHandler>? _viewHandlerReference;

		internal IViewHandler? ViewHandler
		{
			get => _viewHandlerReference != null && _viewHandlerReference.TryGetTarget(out var v) ? v : throw new InvalidOperationException("ViewHandler is null");
			set => _viewHandlerReference = value == null ? null : new WeakReference<IViewHandler>(value);
		}

		ICrossPlatformLayout? CrossPlatformLayout => ViewHandler?.VirtualView as ICrossPlatformLayout;

		double _lastMeasureHeight = double.NaN;
		double _lastMeasureWidth = double.NaN;
		CGSize _lastSizeThatFits;

		CAShapeLayer? _maskLayer;
		CAShapeLayer? _backgroundMaskLayer;
		CAShapeLayer? _shadowLayer;
		[UnconditionalSuppressMessage("Memory", "MEM0002", Justification = "_borderView is a SubView")]
		UIView? _borderView;

		public WrapperView()
		{
		}

		public WrapperView(CGRect frame)
			: base(frame)
		{
		}

		internal bool IsMeasureValid(double widthConstraint, double heightConstraint)
		{
			// Check the last constraints this View was measured with; if they're the same,
			// then the current measure info is already correct and we don't need to repeat it
			return heightConstraint == _lastMeasureHeight && widthConstraint == _lastMeasureWidth;
		}

		internal void CacheMeasureConstraints(double widthConstraint, double heightConstraint)
		{
			_lastMeasureWidth = widthConstraint;
			_lastMeasureHeight = heightConstraint;
		}

		CAShapeLayer? MaskLayer
		{
			get => _maskLayer;
			set
			{
				var layer = GetLayer();

				if (layer is not null && _maskLayer is not null)
					layer.Mask = null;

				_maskLayer = value;

				if (layer is not null)
					layer.Mask = value;
			}
		}

		CAShapeLayer? BackgroundMaskLayer
		{
			get => _backgroundMaskLayer;
			set
			{
				var backgroundLayer = GetBackgroundLayer();

				if (backgroundLayer is not null && _backgroundMaskLayer is not null)
					backgroundLayer.Mask = null;

				_backgroundMaskLayer = value;

				if (backgroundLayer is not null)
					backgroundLayer.Mask = value;
			}
		}

		CAShapeLayer? ShadowLayer
		{
			get => _shadowLayer;
			set
			{
				_shadowLayer?.RemoveFromSuperLayer();
				_shadowLayer = value;

				if (_shadowLayer != null)
					Layer.InsertSublayer(_shadowLayer, 0);
			}
		}

		public override void SetNeedsLayout()
		{
			InvalidateConstraintsCache();
			base.SetNeedsLayout();
		}

		public override void LayoutSubviews()
		{
			base.LayoutSubviews();

			var subviews = Subviews;
			if (subviews.Length == 0)
				return;

			if (_borderView is not null)
				BringSubviewToFront(_borderView);

			var child = subviews[0];

			child.Frame = Bounds;

			if (MaskLayer is not null)
				MaskLayer.Frame = Bounds;

			if (BackgroundMaskLayer is not null)
				BackgroundMaskLayer.Frame = Bounds;

			if (ShadowLayer is not null)
				ShadowLayer.Frame = Bounds;

			if (_borderView is not null)
				_borderView.Frame = Bounds;

			SetClip();
			SetShadow();
			SetBorder();

			double boundWidth = Bounds.Width;
			double boundHeight = Bounds.Height;

			if (!IsMeasureValid(boundWidth, boundHeight))
			{
				CacheMeasureConstraints(boundWidth, boundHeight);
				_lastSizeThatFits = GetSizeThatFits(Bounds.Size);
			}

			CrossPlatformLayout?.CrossPlatformArrange(Bounds.ToRectangle());
		}

		internal void Disconnect()
		{
			MaskLayer = null;
			BackgroundMaskLayer = null;
			ShadowLayer = null;
			_borderView?.RemoveFromSuperview();
		}


		// TODO obsolete or delete this for NET9
		public new void Dispose()
		{
			Disconnect();
			base.Dispose();
		}

		public override CGSize SizeThatFits(CGSize size)
		{
			double widthConstraint = size.Width;
			double heightConstraint = size.Height;

			if (IsMeasureValid(widthConstraint, heightConstraint))
			{
				return _lastSizeThatFits;
			}

			var returnSize = GetSizeThatFits(size);

			// SizeThatFits might be invoked when the view is still off-screen, so caching the constraints is avoided here.
			// Constraints should not be cached when the view is measured off-screen, as they may differ when the view is on-screen (e.g., in a Label).
			if (Window is not null)
			{
				CacheMeasureConstraints(widthConstraint, heightConstraint);
				_lastSizeThatFits = returnSize;
			}

			return returnSize;
		}

		CGSize GetSizeThatFits(CGSize size)
		{
			var subviews = Subviews;
			
			CGSize returnSize;
			if (subviews.Length == 0)
			{
				returnSize = base.SizeThatFits(size);
			}
			else
			{
				var child = subviews[0];

				if (CrossPlatformLayout is { } crossPlatformLayout)
				{
					returnSize = CrossPlatformMeasure(size, crossPlatformLayout);
				}
				// Calling SizeThatFits on an ImageView always returns the image's dimensions, so we need to call the extension method
				// This also affects ImageButtons
				else if (child is UIImageView imageView)
				{
					returnSize = imageView.SizeThatFitsImage(size);
				}
				else if (child is UIButton { ImageView.Image: not null, CurrentTitle: null } imageButton)
				{
					returnSize = imageButton.ImageView.SizeThatFitsImage(size);
				}
				else
				{
					returnSize = child.SizeThatFits(size);
				}
			}

			return returnSize;
		}

		static CGSize CrossPlatformMeasure(CGSize size, ICrossPlatformLayout crossPlatformLayout)
		{
			double widthConstraint = size.Width;
			double heightConstraint = size.Height;

			if (crossPlatformLayout is IView view)
			{
				widthConstraint = IsExplicitSet(view.Width) ? view.Width : widthConstraint;
				heightConstraint = IsExplicitSet(view.Height) ? view.Height : heightConstraint;
			}

			return crossPlatformLayout.CrossPlatformMeasure(widthConstraint, heightConstraint).ToCGSize();
		}

		partial void ClipChanged()
		{
			SetClip();
		}

		partial void ShadowChanged()
		{
			SetShadow();
		}

		partial void BorderChanged() => SetBorder();

		void InvalidateConstraintsCache()
		{
			_lastMeasureWidth = double.NaN;
			_lastMeasureHeight = double.NaN;
		}

		void SetClip()
		{
			var mask = MaskLayer;
			var backgroundMask = BackgroundMaskLayer;

			if (mask is null && Clip is null)
				return;

			var frame = Frame;
			var bounds = new RectF(0, 0, (float)frame.Width, (float)frame.Height);
			var path = _clip?.PathForBounds(bounds);
			var nativePath = path?.AsCGPath();

			mask ??= MaskLayer = new StaticCAShapeLayer();
			mask.Path = nativePath;

			var backgroundLayer = GetBackgroundLayer();

			// We wrap some controls for certain visual effects like applying background gradient etc.
			// For this reason, we have to clip the background layer as well if it exists.
			if (backgroundLayer is null)
				return;

			backgroundMask ??= BackgroundMaskLayer = new StaticCAShapeLayer();
			backgroundMask.Path = nativePath;
		}

		void SetShadow()
		{
			var shadowLayer = ShadowLayer;

			if (shadowLayer == null && Shadow == null)
				return;

			shadowLayer ??= ShadowLayer = new StaticCAShapeLayer();

			var frame = Frame;
			var bounds = new RectF(0, 0, (float)frame.Width, (float)frame.Height);

			shadowLayer.FillColor = new CGColor(0, 0, 0, 1);

			var path = _clip?.PathForBounds(bounds);
			var nativePath = path?.AsCGPath();
			shadowLayer.Path = nativePath;

			if (Shadow == null)
				shadowLayer.ClearShadow();
			else
				shadowLayer.SetShadow(Shadow);
		}

		void SetBorder()
		{
			if (Border == null)
			{
				_borderView?.RemoveFromSuperview();
				return;
			}

			if (_borderView is null)
			{
				AddSubview(_borderView = new UIView(Bounds) { UserInteractionEnabled = false });
			}

			_borderView.UpdateMauiCALayer(Border);
		}

		CALayer? GetLayer()
		{
			var sublayers = Layer?.Sublayers;
			if (sublayers is null)
				return null;

			foreach (var subLayer in sublayers)
				if (subLayer.Delegate is not null)
					return subLayer;

			return Layer;
		}

		CALayer? GetBackgroundLayer()
		{
			var sublayers = Layer?.Sublayers;
			if (sublayers is null)
				return null;

			foreach (var subLayer in sublayers)
				if (subLayer.Name == ViewExtensions.BackgroundLayerName)
					return subLayer;

			return Layer;
		}

		void IMauiPlatformView.InvalidateAncestorsMeasuresWhenMovedToWindow()
		{
			_invalidateParentWhenMovedToWindow = true;
		}

		void IMauiPlatformView.InvalidateMeasure(bool isPropagating) => SetNeedsLayout();

		[UnconditionalSuppressMessage("Memory", "MEM0002", Justification = IUIViewLifeCycleEvents.UnconditionalSuppressMessage)]
		EventHandler? _movedToWindow;
		event EventHandler? IUIViewLifeCycleEvents.MovedToWindow
		{
			add => _movedToWindow += value;
			remove => _movedToWindow -= value;
		}

		public override void MovedToWindow()
		{
			base.MovedToWindow();
			_movedToWindow?.Invoke(this, EventArgs.Empty);
			if (_invalidateParentWhenMovedToWindow)
			{
				_invalidateParentWhenMovedToWindow = false;
				this.InvalidateAncestorsMeasures();
			}
		}
	}
}