/*
 * Greenshot - a free and open source screenshot tool
 * Copyright (C) 2007-2011  Thomas Braun, Jens Klingen, Robin Krom
 * 
 * For more information see: http://getgreenshot.org/
 * The Greenshot project is hosted on Sourceforge: http://sourceforge.net/projects/greenshot/
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 1 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

using Greenshot.Configuration;
using Greenshot.Drawing.Fields;
using Greenshot.Drawing.Filters;
using Greenshot.Helpers;
using Greenshot.Plugin;
using GreenshotPlugin.Core;
using Greenshot.Plugin.Drawing;
using Greenshot.Memento;
using IniFile;

namespace Greenshot.Drawing {
	/// <summary>
	/// represents a rectangle, ellipse, label or whatever. Can contain filters, too.
	/// serializable for clipboard support
	/// Subclasses should fulfill INotifyPropertyChanged contract, i.e. call
	/// OnPropertyChanged whenever a public property has been changed.
	/// </summary>
	[Serializable()]
	public abstract class DrawableContainer : AbstractFieldHolderWithChildren, INotifyPropertyChanged, IDrawableContainer {
		private static readonly log4net.ILog LOG = log4net.LogManager.GetLogger(typeof(DrawableContainer));
		private static System.ComponentModel.ComponentResourceManager editorFormResources = new System.ComponentModel.ComponentResourceManager(typeof(ImageEditorForm));
		private static CoreConfiguration conf = IniConfig.GetIniSection<CoreConfiguration>();

		private bool isMadeUndoable = false;
		
		[NonSerialized]
		private PropertyChangedEventHandler propertyChanged;
		public event PropertyChangedEventHandler PropertyChanged {
			add { propertyChanged += value; }
			remove{ propertyChanged -= value; }
		}
		
		public List<IFilter> Filters {
			get {
				List<IFilter> ret = new List<IFilter>();
				foreach(IFieldHolder c in Children) {
					if (c is IFilter) {
						ret.Add(c as IFilter);
					}
				}
				return ret;
			}
		}
			
		[NonSerialized]
		internal Surface parent;
		public ISurface Parent {
			get { return parent; }
			set { SwitchParent((Surface)value); }
		}
		[NonSerialized]
		protected Gripper[] grippers;
		private bool layoutSuspended = false;
		
		[NonSerialized]
		private bool selected = false;
		public bool Selected {
			get {return selected;}
			set {
				selected = value;
				OnPropertyChanged("Selected");
			}
		}
		
		[NonSerialized]
		public EditStatus Status = EditStatus.UNDRAWN;
		
		private int left = 0;
		public int Left {
			get { return left; }
			set {
				if(value != left) {
					left = value;
					DoLayout();
				}
			}
		}
		
		private int top = 0;
		public int Top {
			get { return top; }
			set {
				if(value != top) {
					top = value;
					DoLayout();
				}
			}
		}
		
		private int width = 0;
		public int Width {
			get { return width; }
			set {
				if(value != width) {
					width = value;
					DoLayout();
				}
			}
		}
		
		private int height = 0;
		public int Height {
			get { return height; }
			set {
				if(value != height) {
					height = value;
					DoLayout();
				}
			}
		}
		
		public Point Location {
			get {
				return new Point(left, top);
			}
		}

		public Size Size {
			get {
				return new Size(width, height);
			}
		}

		[NonSerialized]
		/// <summary>
		/// will store current bounds of this DrawableContainer before starting a resize
		/// </summary>
		private Rectangle boundsBeforeResize = Rectangle.Empty;
		
		[NonSerialized]
		/// <summary>
		/// "workbench" rectangle - used for calculatoing bounds during resizing (to be applied to this DrawableContainer afterwards)
		/// </summary>
		private RectangleF boundsAfterResize = RectangleF.Empty;
		
		public Rectangle Bounds {
			get { return GuiRectangle.GetGuiRectangle(Left, Top, Width, Height); }
			set {
				Left = round(value.Left);
				Top = round(value.Top);
				Width = round(value.Width);
				Height = round(value.Height);
			}
		}
		
		public virtual void ApplyBounds(RectangleF newBounds) {
			Left = round(newBounds.Left);
			Top = round(newBounds.Top);
			Width = round(newBounds.Width);
			Height = round(newBounds.Height);
		}
		
		public DrawableContainer(Surface parent) {
			this.parent = parent;
			InitControls();
		}

		public void Add(IFilter filter) {
			AddChild(filter);
		}
		
		public void Remove(IFilter filter) {
			RemoveChild(filter);
		}
		
		private int round(float f) {
			if(float.IsPositiveInfinity(f) || f>int.MaxValue/2) return int.MaxValue/2;
			else if (float.IsNegativeInfinity(f) || f<int.MinValue/2) return int.MinValue/2;
			return (int)Math.Round(f);
		}
		
		private int round(double d) {
			if(Double.IsPositiveInfinity(d) || d>int.MaxValue/2) return int.MaxValue/2;
			else if (Double.IsNegativeInfinity(d) || d<int.MinValue/2) return int.MinValue/2;
			else return (int)Math.Round(d);
		}

		private bool accountForShadowChange = false;
		public virtual Rectangle DrawingBounds {
			get {
				foreach(IFilter filter in Filters) {
					if (filter.Invert) {
						return new Rectangle(Point.Empty, parent.Image.Size);
					}
				}
				// Take a base safetymargin
				int lineThickness = 5;
				if (HasField(FieldType.LINE_THICKNESS)) {
					lineThickness += GetFieldValueAsInt(FieldType.LINE_THICKNESS);
				}
				int offset = lineThickness/2;

				int shadow = 0;
				if (accountForShadowChange || (HasField(FieldType.SHADOW) && GetFieldValueAsBool(FieldType.SHADOW))){
					accountForShadowChange = false;
					shadow += 10;
				}
				return new Rectangle(Bounds.Left-offset, Bounds.Top-offset, Bounds.Width+lineThickness+shadow, Bounds.Height+lineThickness+shadow);
			}
		}

		public virtual void Invalidate() {
			parent.Invalidate(DrawingBounds);
		}
		
		public void AlignToParent(HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment) {
			
			int lineThickness = GetFieldValueAsInt(FieldType.LINE_THICKNESS);
			if (horizontalAlignment == HorizontalAlignment.Left) {
				Left = lineThickness/2;
			}
			if (horizontalAlignment == HorizontalAlignment.Right) {
				Left = parent.Width - this.Width - lineThickness/2;
			}
			if (horizontalAlignment == HorizontalAlignment.Center) {
				Left = (parent.Width / 2) - (this.Width / 2) - lineThickness/2;
			}

			if (verticalAlignment == VerticalAlignment.TOP) {
				Top = lineThickness/2;
			}
			if (verticalAlignment == VerticalAlignment.BOTTOM) {
				Top = parent.Height - this.Height - lineThickness/2;
			}
			if (verticalAlignment == VerticalAlignment.CENTER) {
				Top = (parent.Height / 2) - (this.Height / 2) - lineThickness/2;
			}
		}
		
		public virtual bool InitContent() { return true; }
		
		public virtual void OnDoubleClick() {}
		
		private void InitControls() {
			InitGrippers();
			
			DoLayout();
		}
		
		protected void InitGrippers() {
			grippers = new Gripper[8];
			for(int i=0; i<grippers.Length; i++) {
				grippers[i] = new Gripper();
				grippers[i].Position = i;
				grippers[i].MouseDown += new MouseEventHandler(gripperMouseDown);
				grippers[i].MouseUp += new MouseEventHandler(gripperMouseUp);
				grippers[i].MouseMove += new MouseEventHandler(gripperMouseMove);
				grippers[i].Visible = false;
				grippers[i].Parent = parent;
			}
			grippers[Gripper.POSITION_TOP_CENTER].Cursor = Cursors.SizeNS;
			grippers[Gripper.POSITION_MIDDLE_RIGHT].Cursor = Cursors.SizeWE;
			grippers[Gripper.POSITION_BOTTOM_CENTER].Cursor = Cursors.SizeNS;
			grippers[Gripper.POSITION_MIDDLE_LEFT].Cursor = Cursors.SizeWE;
			if (parent != null) {
				parent.Controls.AddRange(grippers); // otherwise we'll attach them in switchParent
			}
		}
		
		public void SuspendLayout() {
			layoutSuspended = true;
		}
		
		public void ResumeLayout() {
			layoutSuspended = false;
			DoLayout();
		}
		
		protected virtual void DoLayout() {
			if (!layoutSuspended) {
				int[] xChoords = new int[]{this.Left-2,this.Left+this.Width/2-2,this.Left+this.Width-2};
				int[] yChoords = new int[]{this.Top-2,this.Top+this.Height/2-2,this.Top+this.Height-2};

				grippers[Gripper.POSITION_TOP_LEFT].Left = xChoords[0]; grippers[Gripper.POSITION_TOP_LEFT].Top = yChoords[0];
				grippers[Gripper.POSITION_TOP_CENTER].Left = xChoords[1]; grippers[Gripper.POSITION_TOP_CENTER].Top = yChoords[0];
				grippers[Gripper.POSITION_TOP_RIGHT].Left = xChoords[2]; grippers[Gripper.POSITION_TOP_RIGHT].Top = yChoords[0];
				grippers[Gripper.POSITION_MIDDLE_RIGHT].Left = xChoords[2]; grippers[Gripper.POSITION_MIDDLE_RIGHT].Top = yChoords[1];
				grippers[Gripper.POSITION_BOTTOM_RIGHT].Left = xChoords[2]; grippers[Gripper.POSITION_BOTTOM_RIGHT].Top = yChoords[2];
				grippers[Gripper.POSITION_BOTTOM_CENTER].Left = xChoords[1]; grippers[Gripper.POSITION_BOTTOM_CENTER].Top = yChoords[2];
				grippers[Gripper.POSITION_BOTTOM_LEFT].Left = xChoords[0]; grippers[Gripper.POSITION_BOTTOM_LEFT].Top = yChoords[2];
				grippers[Gripper.POSITION_MIDDLE_LEFT].Left = xChoords[0]; grippers[Gripper.POSITION_MIDDLE_LEFT].Top = yChoords[1];
				
				if((grippers[Gripper.POSITION_TOP_LEFT].Left < grippers[Gripper.POSITION_BOTTOM_RIGHT].Left && grippers[Gripper.POSITION_TOP_LEFT].Top < grippers[Gripper.POSITION_BOTTOM_RIGHT].Top) ||
					grippers[Gripper.POSITION_TOP_LEFT].Left > grippers[Gripper.POSITION_BOTTOM_RIGHT].Left && grippers[Gripper.POSITION_TOP_LEFT].Top > grippers[Gripper.POSITION_BOTTOM_RIGHT].Top) {
					grippers[Gripper.POSITION_TOP_LEFT].Cursor = Cursors.SizeNWSE;
					grippers[Gripper.POSITION_TOP_RIGHT].Cursor = Cursors.SizeNESW;
					grippers[Gripper.POSITION_BOTTOM_RIGHT].Cursor = Cursors.SizeNWSE;
					grippers[Gripper.POSITION_BOTTOM_LEFT].Cursor = Cursors.SizeNESW;
				} else if((grippers[Gripper.POSITION_TOP_LEFT].Left > grippers[Gripper.POSITION_BOTTOM_RIGHT].Left && grippers[Gripper.POSITION_TOP_LEFT].Top < grippers[Gripper.POSITION_BOTTOM_RIGHT].Top) ||
					grippers[Gripper.POSITION_TOP_LEFT].Left < grippers[Gripper.POSITION_BOTTOM_RIGHT].Left && grippers[Gripper.POSITION_TOP_LEFT].Top > grippers[Gripper.POSITION_BOTTOM_RIGHT].Top) {
					grippers[Gripper.POSITION_TOP_LEFT].Cursor = Cursors.SizeNESW;
					grippers[Gripper.POSITION_TOP_RIGHT].Cursor = Cursors.SizeNWSE;
					grippers[Gripper.POSITION_BOTTOM_RIGHT].Cursor = Cursors.SizeNESW;
					grippers[Gripper.POSITION_BOTTOM_LEFT].Cursor = Cursors.SizeNWSE;
				} else if (grippers[Gripper.POSITION_TOP_LEFT].Left == grippers[Gripper.POSITION_BOTTOM_RIGHT].Left) {
					grippers[Gripper.POSITION_TOP_LEFT].Cursor = Cursors.SizeNS;
					grippers[Gripper.POSITION_BOTTOM_RIGHT].Cursor = Cursors.SizeNS;
				} else if (grippers[Gripper.POSITION_TOP_LEFT].Top == grippers[Gripper.POSITION_BOTTOM_RIGHT].Top) {
					grippers[Gripper.POSITION_TOP_LEFT].Cursor = Cursors.SizeWE;
					grippers[Gripper.POSITION_BOTTOM_RIGHT].Cursor = Cursors.SizeWE;
				}
			}
		}
		
		public virtual void Dispose() {
			for(int i=0; i<grippers.Length; i++) {
				grippers[i].Dispose();
			}
			
			FieldAggregator aggProps = parent.FieldAggregator;
			aggProps.UnbindElement(this);
		}
		
		int mx;
		int my;
		private void gripperMouseDown(object sender, MouseEventArgs e) {
			mx = e.X;
			my = e.Y;
			Status = EditStatus.RESIZING;
			boundsBeforeResize = new Rectangle(left, top, width, height);
			boundsAfterResize = new RectangleF(boundsBeforeResize.Left, boundsBeforeResize.Top, boundsBeforeResize.Width, boundsBeforeResize.Height);
			isMadeUndoable = false;
		}

		private void gripperMouseUp(object sender, MouseEventArgs e) {
			Status = EditStatus.IDLE;
			boundsBeforeResize = Rectangle.Empty;
			boundsAfterResize = RectangleF.Empty;
			isMadeUndoable = false;
			Invalidate();
		}
		
		private void gripperMouseMove(object sender, MouseEventArgs e) {
			if(Status.Equals(EditStatus.RESIZING)) {
				// check if we already made this undoable
				if (!isMadeUndoable) {
					// don't allow another undo until we are finished with this move
					isMadeUndoable = true;
					// Make undo-able
					MakeBoundsChangeUndoable(false);
				}
				
				Invalidate();
				SuspendLayout();
				
				Gripper gr = (Gripper)sender;
				int absX = gr.Left + e.X;
				int absY = gr.Top + e.Y;

				// reset "workbench" rectangle to current bounds
				boundsAfterResize.X = boundsBeforeResize.X;
				boundsAfterResize.Y = boundsBeforeResize.Y;
				boundsAfterResize.Width = boundsBeforeResize.Width;
				boundsAfterResize.Height = boundsBeforeResize.Height;

				// calculate scaled rectangle
				ScaleHelper.Scale(ref boundsAfterResize, gr.Position, new PointF(absX, absY), ScaleHelper.GetScaleOptions());

				// apply scaled bounds to this DrawableContainer
				ApplyBounds(boundsAfterResize);
	            
				ResumeLayout();
				Invalidate();
			}
		}
		
		private void childLabelMouseMove(object sender, MouseEventArgs e) {
			if (Status.Equals(EditStatus.RESIZING)) {
				Invalidate();
				SuspendLayout();
				this.Left += e.X - mx;
				this.Top += e.Y - my;
				ResumeLayout();
				Invalidate();
			}
		}
		
		public bool hasFilters {
			get {
				return Filters.Count > 0;
			}
		}

		public abstract void Draw(Graphics graphics, RenderMode renderMode);
		
		public virtual void DrawContent(Graphics graphics, Bitmap bmp, RenderMode renderMode, Rectangle clipRectangle) {
			if (Children.Count > 0) {
				if(Status != EditStatus.IDLE) {
					DrawSelectionBorder(graphics, Bounds);
				} else {
					if (clipRectangle.Width != 0 && clipRectangle.Height != 0) {
						foreach(IFilter filter in Filters) {
							if (filter.Invert) {
								filter.Apply(graphics, bmp, Bounds, renderMode);
							} else {
								Rectangle drawingRect = new Rectangle(Bounds.Location, Bounds.Size);
								drawingRect.Intersect(clipRectangle);
								filter.Apply(graphics, bmp, drawingRect, renderMode);
							}
						}
					}
	
				}
			}
			Draw(graphics, renderMode);
		}
		
		public virtual bool Contains(int x, int y) {
			return Bounds.Contains(x , y);
		}
		
		public virtual bool ClickableAt(int x, int y) {
			Rectangle r = GuiRectangle.GetGuiRectangle(Left, Top, Width, Height);
			r.Inflate(5, 5);
			return r.Contains(x, y);
		}
		
		protected void DrawSelectionBorder(Graphics g, Rectangle rect) {
			using (Pen pen = new Pen(Color.MediumSeaGreen)) {
				pen.DashPattern = new float[]{1,2};
				pen.Width = 1;
				g.DrawRectangle(pen, rect);
			}
		}
		
		public virtual void ShowGrippers() {
			for (int i=0; i<grippers.Length; i++) {
				if(grippers[i].Enabled) grippers[i].Show();
				else grippers[i].Hide();
			}
			this.ResumeLayout();
		}
		
		public void HideGrippers() {
			this.SuspendLayout();
			for (int i=0; i<grippers.Length; i++) {
				grippers[i].Hide();
			}
		}
		
		public void ResizeTo(int width, int height, int anchorPosition) {
			this.SuspendLayout();
			Width = width;
			Height = height;
			this.ResumeLayout();
		}

		/// <summary>
		/// Make a following bounds change on this drawablecontainer undoable!
		/// </summary>
		/// <param name="allowMerge">true means allow the moves to be merged</param>
		public void MakeBoundsChangeUndoable(bool allowMerge) {
			this.parent.MakeUndoable(new DrawableContainerBoundsChangeMemento(this), allowMerge);
		}
		
		public void MoveBy(int dx, int dy) {
			this.SuspendLayout();
			this.Left += dx;
			this.Top += dy;
			this.ResumeLayout();
		}
		
		/// <summary>
		/// A handler for the MouseDown, used if you don't want the surface to handle this for you
		/// </summary>
		/// <param name="x">current mouse x</param>
		/// <param name="y">current mouse y</param>
		/// <returns>true if the event is handled, false if the surface needs to handle it</returns>
		public virtual bool HandleMouseDown(int x, int y) {
			Left = boundsBeforeResize.X = x;
			Top = boundsBeforeResize.Y = y;
			return true;
		}

		/// <summary>
		/// A handler for the MouseMove, used if you don't want the surface to handle this for you
		/// </summary>
		/// <param name="x">current mouse x</param>
		/// <param name="y">current mouse y</param>
		/// <returns>true if the event is handled, false if the surface needs to handle it</returns>
		public virtual bool HandleMouseMove(int x, int y) {
			Invalidate();
			SuspendLayout();
			
			// reset "workrbench" rectangle to current bounds
			boundsAfterResize.X = boundsBeforeResize.Left;
			boundsAfterResize.Y = boundsBeforeResize.Top;
			boundsAfterResize.Width = x - boundsAfterResize.Left;
			boundsAfterResize.Height = y - boundsAfterResize.Top;
			
			ScaleHelper.Scale(boundsBeforeResize, x, y, ref boundsAfterResize, GetAngleRoundProcessor());
			
			// apply scaled bounds to this DrawableContainer
			ApplyBounds(boundsAfterResize);
			
			ResumeLayout();
			Invalidate();
			return true;
		}
		
		/// <summary>
		/// A handler for the MouseUp
		/// </summary>
		/// <param name="x">current mouse x</param>
		/// <param name="y">current mouse y</param>
		public virtual void HandleMouseUp(int x, int y) {
		}
		
		private void SwitchParent(Surface newParent) {
			if (parent != null) {
				for (int i=0; i<grippers.Length; i++) {
					parent.Controls.Remove(grippers[i]);
				}
			} else if(grippers == null) {
				InitControls();
			}
			parent = newParent;
			parent.Controls.AddRange(grippers);
			foreach(IFilter filter in Filters) {
				filter.Parent = this;
			}
		}
		
		// drawablecontainers are regarded equal if they are of the same type and their bounds are equal. this should be sufficient.
		public override bool Equals(object obj) {
			bool ret = false;
			if (obj != null && GetType().Equals(obj.GetType())) {
				DrawableContainer other = obj as DrawableContainer;
				if (left==other.left && top==other.top && width==other.width && height==other.height) {
					ret = true;
				}
			}
			return ret;
		}
		
		public override int GetHashCode() {
			return left.GetHashCode() ^ top.GetHashCode() ^ width.GetHashCode() ^ height.GetHashCode() ^ GetFields().GetHashCode();
		}
		
		protected void OnPropertyChanged(string propertyName) {
			if (propertyChanged != null) {
				propertyChanged(this, new PropertyChangedEventArgs(propertyName));
				Invalidate();
			}
		}
		
		/// <summary>
		/// This method will be called before a field is changes.
		/// Using this makes it possible to invalidate the object as is before changing.
		/// </summary>
		/// <param name="fieldToBeChanged">The field to be changed</param>
		/// <param name="newValue">The new value</param>
		public virtual void BeforeFieldChange(Field fieldToBeChanged, object newValue) {
			parent.MakeUndoable(new ChangeFieldHolderMemento(this, fieldToBeChanged), true);
			Invalidate();
		}
		
		/// <summary>
		/// Handle the field changed event, this should invalidate the correct bounds (e.g. when shadow comes or goes more pixels!)
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		public void HandleFieldChanged(object sender, FieldChangedEventArgs e) {
			LOG.DebugFormat("Field {0} changed", e.Field.FieldType);
			if (e.Field.FieldType == FieldType.SHADOW) {
				accountForShadowChange = true;
			}
			Invalidate();
		}
		
		protected virtual ScaleHelper.IDoubleProcessor GetAngleRoundProcessor() {
			return ScaleHelper.ShapeAngleRoundBehavior.Instance;
		}
		
		/// <summary>
		/// Add items to a context menu for the selected item
		/// </summary>
		/// <param name="menu"></param>
		public virtual void AddContextMenuItems(ContextMenuStrip menu) {
			DrawableContainerList myselfAsList = new DrawableContainerList();
			myselfAsList.Add(this);
 			ILanguage lang = Language.GetInstance();
			bool push = parent.Elements.CanPushDown(myselfAsList);
			bool pull = parent.Elements.CanPullUp(myselfAsList);

			ToolStripMenuItem item;

			// Pull "up"
			if (pull) {
				item = new ToolStripMenuItem(lang.GetString(LangKey.editor_uptotop));
				item.Click += delegate {
					parent.Elements.PullElementsToTop(myselfAsList);
					parent.Elements.Invalidate();
				};
				menu.Items.Add(item);
				item = new ToolStripMenuItem(lang.GetString(LangKey.editor_uponelevel));
				item.Click += delegate {
					parent.Elements.PullElementsUp(myselfAsList);
					parent.Elements.Invalidate();
				};
				menu.Items.Add(item);
			}
			// Push "down"
			if (push) {
				item = new ToolStripMenuItem(lang.GetString(LangKey.editor_downtobottom));
				item.Click += delegate {
					parent.Elements.PushElementsToBottom(myselfAsList);
					parent.Elements.Invalidate();
				};
				menu.Items.Add(item);
				item = new ToolStripMenuItem(lang.GetString(LangKey.editor_downonelevel));
				item.Click += delegate {
					parent.Elements.PushElementsDown(myselfAsList);
					parent.Elements.Invalidate();
				};
				menu.Items.Add(item);
			}

			// Duplicate
			item = new ToolStripMenuItem(lang.GetString(LangKey.editor_duplicate));
			item.Click += delegate {
				DrawableContainerList dcs = myselfAsList.Clone();
				dcs.Parent = parent;
				dcs.MoveBy(10,10);
				parent.AddElements(dcs);
				parent.DeselectAllElements();
				parent.SelectElements(dcs);
			};
			menu.Items.Add(item);

			// Copy
			item = new ToolStripMenuItem(lang.GetString(LangKey.editor_copytoclipboard));
			item.Image = ((System.Drawing.Image)(editorFormResources.GetObject("copyToolStripMenuItem.Image")));
			item.Click += delegate {
				ClipboardHelper.SetClipboardData(typeof(DrawableContainerList), myselfAsList);
			};
			menu.Items.Add(item);

			// Cut
			item = new ToolStripMenuItem(lang.GetString(LangKey.editor_cuttoclipboard));
			item.Image = ((System.Drawing.Image)(editorFormResources.GetObject("editor_cuttoclipboard.Image")));
			item.Click += delegate {
				ClipboardHelper.SetClipboardData(typeof(DrawableContainerList), myselfAsList);
				parent.RemoveElement(this, true);
			};
			menu.Items.Add(item);

			// Delete
			item = new ToolStripMenuItem(lang.GetString(LangKey.editor_deleteelement));
			item.Image = ((System.Drawing.Image)(editorFormResources.GetObject("removeObjectToolStripMenuItem.Image")));
			item.Click += delegate {
				parent.RemoveElement(this, true);
			};
			menu.Items.Add(item);
		}

		public virtual void ShowContextMenu(MouseEventArgs e) {
			if (conf.isExperimentalFeatureEnabled("Contextmenu")) {
				ContextMenuStrip menu = new ContextMenuStrip();
				AddContextMenuItems(menu);
				if (menu.Items.Count > 0) {
					menu.Show(parent, e.Location);
				}
			}
		}
	}
}