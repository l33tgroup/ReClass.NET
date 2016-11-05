﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ReClassNET.DataExchange;
using ReClassNET.Memory;
using ReClassNET.Nodes;
using ReClassNET.Util;

namespace ReClassNET.UI
{
	partial class MemoryViewControl : ScrollableCustomControl
	{
		private ClassNode classNode;

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ClassNode ClassNode
		{
			get { return classNode; }
			set
			{
				ClearSelection();

				OnSelectionChanged();

				classNode = value;

				VerticalScroll.Value = 0;
				if (classNode != null && Memory != null)
				{
					classNode.UpdateAddress(Memory);
				}
				Invalidate();
			}
		}

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public MemoryBuffer Memory { get; set; }

		private readonly List<HotSpot> hotSpots = new List<HotSpot>();
		private readonly List<HotSpot> selectedNodes = new List<HotSpot>();

		public IEnumerable<BaseNode> SelectedNodes => selectedNodes.Select(s => s.Node);

		private HotSpot selectionCaret = null;
		private HotSpot selectionAnchor = null;

		public event EventHandler SelectionChanged;

		private readonly FontEx font;

		public MemoryViewControl()
		{
			InitializeComponent();

			font = new FontEx
			{
				Font = new Font("Courier New", DpiUtil.ScaleIntX(13), GraphicsUnit.Pixel),
				Width = DpiUtil.ScaleIntX(8),
				Height = DpiUtil.ScaleIntY(16)
			};

			editBox.Font = font;
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);

			VerticalScroll.Enabled = true;
			VerticalScroll.Visible = true;
			VerticalScroll.SmallChange = 10;
			HorizontalScroll.Enabled = false;
			HorizontalScroll.Visible = false;
		}

		internal void RegisterNodeType(Type type, string name, Image icon)
		{
			Contract.Requires(type != null);
			Contract.Requires(name != null);
			Contract.Requires(icon != null);

			var item = new TypeToolStripMenuItem
			{
				Image = icon,
				Text = name,
				Value = type
			};
			item.Click += memoryTypeToolStripMenuItem_Click;

			changeTypeToolStripMenuItem.DropDownItems.Add(item);
		}

		internal void DeregisterNodeType(Type type)
		{
			Contract.Requires(type != null);

			var item = changeTypeToolStripMenuItem.DropDownItems.OfType<TypeToolStripMenuItem>().Where(i => i.Value == type).FirstOrDefault();
			if (item != null)
			{
				item.Click -= memoryTypeToolStripMenuItem_Click;
				changeTypeToolStripMenuItem.DropDownItems.Remove(item);
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);

			if (DesignMode)
			{
				e.Graphics.FillRectangle(Brushes.White, ClientRectangle);

				return;
			}

			hotSpots.Clear();

			using (var brush = new SolidBrush(Program.Settings.BackgroundColor))
			{
				e.Graphics.FillRectangle(brush, ClientRectangle);
			}

			if (ClassNode == null)
			{
				return;
			}

			Memory.Size = ClassNode.MemorySize;
			Memory.Update(ClassNode.Offset);

			var view = new ViewInfo
			{
				Context = e.Graphics,
				Font = font,
				Address = ClassNode.Offset,
				ClientArea = ClientRectangle,
				Level = 0,
				Memory = Memory,
				MultiSelected = selectedNodes.Count > 1,
				HotSpots = hotSpots
			};

			var scrollY = VerticalScroll.Value * font.Height;
			int maxY = 0;
			try
			{
				BaseHexNode.CurrentHighlightTime = DateTime.Now;

				maxY = ClassNode.Draw(view, 0, -scrollY) + scrollY;
			}
			catch
			{
				return;
			}

			/*foreach (var spot in hotSpots.Where(h => h.Type == HotSpotType.Select))
			{
				e.Graphics.DrawRectangle(new Pen(new SolidBrush(Color.FromArgb(150, 255, 0, 0)), 1), spot.Rect);
			}*/

			if (maxY > ClientSize.Height)
			{
				VerticalScroll.Enabled = true;

				VerticalScroll.LargeChange = ClientSize.Height / font.Height;
				VerticalScroll.Maximum = (maxY - ClientSize.Height) / font.Height + VerticalScroll.LargeChange;
			}
			else
			{
				VerticalScroll.Enabled = false;

				VerticalScroll.Value = 0;
			}
		}

		private void OnSelectionChanged()
		{
			SelectionChanged?.Invoke(this, EventArgs.Empty);

			var count = selectedNodes.Count();
			var node = selectedNodes.Select(s => s.Node).FirstOrDefault();

			var nodeIsClass = node is ClassNode;

			addBytesToolStripMenuItem.Enabled = node?.ParentNode != null || nodeIsClass;
			insertBytesToolStripMenuItem.Enabled = count == 1 && node?.ParentNode != null;

			changeTypeToolStripMenuItem.Enabled = count > 0 && !nodeIsClass;

			pasteNodesToolStripMenuItem.Enabled = count == 1 && ReClassClipboard.ContainsData(ReClassClipboard.Format.Nodes);
			removeToolStripMenuItem.Enabled = !nodeIsClass;

			copyAddressToolStripMenuItem.Enabled = !nodeIsClass;
		}

		#region Process Input

		protected override void OnMouseClick(MouseEventArgs e)
		{
			editBox.Visible = false;

			foreach (var hotSpot in hotSpots)
			{
				if (hotSpot.Rect.Contains(e.Location))
				{
					try
					{
						var hitObject = hotSpot.Node;

						if (hotSpot.Type == HotSpotType.OpenClose)
						{
							hitObject.ToggleLevelOpen(hotSpot.Level);
						}
						else if (hotSpot.Type == HotSpotType.Click)
						{
							hitObject.Update(hotSpot);
						}
						else if (hotSpot.Type == HotSpotType.Select)
						{
							if (e.Button == MouseButtons.Left)
							{
								if (ModifierKeys == Keys.None)
								{
									ClearSelection();

									hitObject.IsSelected = true;

									selectedNodes.Add(hotSpot);

									OnSelectionChanged();

									selectionAnchor = selectionCaret = hotSpot;
								}
								else if (ModifierKeys == Keys.Control)
								{
									hitObject.IsSelected = !hitObject.IsSelected;

									if (hitObject.IsSelected)
									{
										selectedNodes.Add(hotSpot);
									}
									else
									{
										selectedNodes.Remove(selectedNodes.Where(c => c.Node == hitObject).FirstOrDefault());
									}

									OnSelectionChanged();
								}
								else if (ModifierKeys == Keys.Shift)
								{
									if (selectedNodes.Count > 0)
									{
										var selectedNode = selectedNodes[0].Node;
										if (hitObject.ParentNode != null && selectedNode.ParentNode != hitObject.ParentNode)
										{
											continue;
										}

										var first = Utils.Min(selectedNodes[0], hotSpot, h => h.Node.Offset.ToInt32());
										var last = first == hotSpot ? selectedNodes[0] : hotSpot;

										ClearSelection();

										var containerNode = selectedNode.ParentNode;
										foreach (var spot in containerNode.Nodes
											.SkipWhile(n => n != first.Node)
											.TakeUntil(n => n == last.Node)
											.Select(n => new HotSpot { Address = containerNode.Offset.Add(n.Offset), Node = n }))
										{
											spot.Node.IsSelected = true;
											selectedNodes.Add(spot);
										}

										OnSelectionChanged();

										selectionAnchor = first;
										selectionCaret = last;
									}
								}
							}
							else if (e.Button == MouseButtons.Right)
							{
								// If there is only one selected node, select the node the user clicked at.
								if (selectedNodes.Count <= 1)
								{
									ClearSelection();

									hitObject.IsSelected = true;

									selectedNodes.Add(hotSpot);

									OnSelectionChanged();

									selectionAnchor = selectionCaret = hotSpot;
								}

								selectedNodeContextMenuStrip.Show(this, e.Location);
							}
						}
						else if (hotSpot.Type == HotSpotType.Drop)
						{
							selectedNodeContextMenuStrip.Show(this, e.Location);
						}
						else if (hotSpot.Type == HotSpotType.Delete)
						{
							RemoveSelectedNodes();
						}
						else if (hotSpot.Type == HotSpotType.ChangeType)
						{
							var refNode = hitObject as BaseReferenceNode;
							if (refNode != null)
							{
								EventHandler changeInnerType = (sender2, e2) =>
								{
									var classNode = (sender2 as TypeToolStripMenuItem)?.Tag as ClassNode;
									if (classNode == null)
									{
										return;
									}

									try
									{
										refNode.ChangeInnerNode(classNode);
									}
									catch (ClassCycleException)
									{
										MessageBox.Show("Can't change node type because this would create a cycle.");
									}
								};

								var menu = new ContextMenuStrip();
								menu.Items.AddRange(
									ClassManager.Classes
									.OrderBy(c => c.Name)
									.Select(c =>
									{
										var b = new TypeToolStripMenuItem
										{
											Text = c.Name,
											Tag = c
										};
										b.Click += changeInnerType;
										return b;
									})
									.ToArray()
								);
								menu.Show(this, e.Location);
							}
						}
					}
					catch (Exception ex)
					{
						ex.ShowDialog();
					}

					Invalidate();
				}
			}

			base.OnMouseClick(e);
		}

		protected override void OnMouseDoubleClick(MouseEventArgs e)
		{
			base.OnMouseDoubleClick(e);

			editBox.Visible = false;

			foreach (var hotSpot in hotSpots.Where(h => h.Type == HotSpotType.Edit))
			{
				if (hotSpot.Rect.Contains(e.Location))
				{
					editBox.BackColor = Program.Settings.SelectedColor;
					editBox.HotSpot = hotSpot;
					editBox.Visible = true;

					break;
				}
			}
		}

		private Point toolTipPosition;
		protected override void OnMouseHover(EventArgs e)
		{
			base.OnMouseHover(e);

			if (selectedNodes.Count > 1)
			{
				var memorySize = selectedNodes.Select(h => h.Node.MemorySize).Sum();
				toolTip.Show($"{selectedNodes.Count} Nodes selected, {memorySize} bytes", this, toolTipPosition.OffsetEx(16, 16));
			}
			else
			{
				foreach (var spot in hotSpots.Where(h => h.Type == HotSpotType.Select))
				{
					if (spot.Rect.Contains(toolTipPosition))
					{
						var text = spot.Node.GetToolTipText(spot, Memory);
						if (!string.IsNullOrEmpty(text))
						{
							toolTip.Show(text, this, toolTipPosition.OffsetEx(16, 16));
						}

						return;
					}
				}
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);

			if (e.Location != toolTipPosition)
			{
				toolTipPosition = e.Location;

				toolTip.Hide(this);

				ResetMouseEventArgs();
			}
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			if (editBox.Visible == false) // Only process keys if the edit field is not visible.
			{
				var key = keyData & Keys.KeyCode;
				var modifier = keyData & Keys.Modifiers;

				if (selectedNodes.Count > 0)
				{
					if (key == Keys.Delete)
					{
						RemoveSelectedNodes();

						return true;
					}
					else if (key == Keys.Menu)
					{
						selectedNodeContextMenuStrip.Show(this, 10, 10);

						return true;
					}
					else if (modifier == Keys.Control && (key == Keys.C || key == Keys.V))
					{
						if (key == Keys.C)
						{
							CopySelectedNodesToClipboard();
						}
						else if (key == Keys.V)
						{
							PasteNodeFromClipboardToSelection();
						}

						return true;
					}
					else if (key == Keys.Down || key == Keys.Up)
					{
						HotSpot toSelect = null;
						if (key == Keys.Down)
						{
							toSelect = hotSpots
								.SkipUntil(h => h.Node == selectionCaret.Node)
								.Where(h => h.Type == HotSpotType.Select)
								.Where(h => h.Node.ParentNode == selectionCaret.Node.ParentNode)
								.FirstOrDefault();
						}
						else
						{
							toSelect = hotSpots
								.TakeWhile(h => h.Node != selectionCaret.Node)
								.Where(h => h.Type == HotSpotType.Select)
								.Where(h => h.Node.ParentNode == selectionCaret.Node.ParentNode)
								.LastOrDefault();
						}

						if (toSelect != null)
						{
							if (modifier != Keys.Shift)
							{
								selectionAnchor = selectionCaret = toSelect;
							}
							else
							{
								selectionCaret = toSelect;
							}

							var first = Utils.Min(selectionAnchor, selectionCaret, h => h.Node.Offset.ToInt32());
							var last = first == selectionAnchor ? selectionCaret : selectionAnchor;

							ClearSelection();

							var containerNode = toSelect.Node.ParentNode;
							foreach (var spot in containerNode.Nodes
								.SkipWhile(n => n != first.Node)
								.TakeUntil(n => n == last.Node)
								.Select(n => new HotSpot { Address = containerNode.Offset.Add(n.Offset), Node = n }))
							{
								spot.Node.IsSelected = true;
								selectedNodes.Add(spot);
							}

							OnSelectionChanged();

							Invalidate();

							return true;
						}
					}
				}
				else if (key == Keys.Down || key == Keys.Up)
				{
					// If no node is selected, try to select the first one.
					var selection = hotSpots
						.Where(h => h.Type == HotSpotType.Select)
						.Where(h => !(h.Node is ClassNode))
						.FirstOrDefault();
					if (selection != null)
					{
						selectionAnchor = selectionCaret = selection;

						selection.Node.IsSelected = true;

						selectedNodes.Add(selection);

						OnSelectionChanged();

						return true;
					}
				}
			}

			return base.ProcessCmdKey(ref msg, keyData);
		}

		#endregion

		#region Event Handler

		private void repaintTimer_Tick(object sender, EventArgs e)
		{
			if (DesignMode)
			{
				return;
			}

			Invalidate(false);
		}

		private void updateClassTimer_Tick(object sender, EventArgs e)
		{
			if (DesignMode)
			{
				return;
			}

			if (ClassNode != null)
			{
				ClassNode.UpdateAddress(Memory);
			}
		}

		private void addBytesToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var item = sender as IntegerToolStripMenuItem;
			if (item == null)
			{
				return;
			}

			AddBytes(item.Value);
		}

		private void insertBytesToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var item = sender as IntegerToolStripMenuItem;
			if (item == null)
			{
				return;
			}

			InsertBytes(item.Value);
		}

		private void memoryTypeToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var item = sender as TypeToolStripMenuItem;
			if (item == null)
			{
				return;
			}

			ReplaceSelectedNodesWithType(item.Value);
		}

		private void removeToolStripMenuItem_Click(object sender, EventArgs e)
		{
			RemoveSelectedNodes();
		}

		private void copyAddressToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (selectedNodes.Count > 0)
			{
				Clipboard.SetText(selectedNodes.First().Address.ToString("X"));
			}
		}

		private void copyNodeToolStripMenuItem_Click(object sender, EventArgs e)
		{
			CopySelectedNodesToClipboard();
		}

		private void pasteNodesToolStripMenuItem_Click(object sender, EventArgs e)
		{
			PasteNodeFromClipboardToSelection();
		}

		private void dissectNodesToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var hexNodes = selectedNodes.Where(h => h.Node is BaseHexNode);
			if (hexNodes.Any())
			{
				NodeDissector.DissectNodes(hexNodes.Select(h => (BaseHexNode)h.Node), Memory);
			}
		}

		#endregion

		public void AddBytes(int length)
		{
			Contract.Requires(length >= 0);

			var hotspot = selectedNodes.FirstOrDefault();
			if (hotspot != null)
			{
				(hotspot.Node.ParentNode ?? hotspot.Node as ClassNode).AddBytes(length);
			}

			Invalidate();
		}

		public void InsertBytes(int length)
		{
			Contract.Requires(length >= 0);

			var hotspot = selectedNodes.FirstOrDefault();
			if (hotspot != null)
			{
				(hotspot.Node.ParentNode ?? hotspot.Node as ClassNode).InsertBytes(hotspot.Node, length);

				Invalidate();
			}
		}

		public void ReplaceSelectedNodesWithType(Type type)
		{
			Contract.Requires(type != null);
			Contract.Requires(type.IsSubclassOf(typeof(BaseNode)));

			var newSelected = new List<HotSpot>(selectedNodes.Count);

			foreach (var selected in selectedNodes.Where(s => !(s.Node is ClassNode)))
			{
				var node = Activator.CreateInstance(type) as BaseNode;

				node.Intialize();

				if (selected.Node.ParentNode.ReplaceChildNode(selected.Node, node))
				{
					node.IsSelected = true;

					var hotspot = new HotSpot
					{
						Address = node.ParentNode.Offset.Add(node.Offset),
						Node = node
					};

					newSelected.Add(hotspot);

					if (selectionAnchor.Node == selected.Node)
					{
						selectionAnchor = hotspot;
					}
					if (selectionCaret.Node == selected.Node)
					{
						selectionCaret = hotspot;
					}
				}
			}

			if (newSelected.Count > 0)
			{
				selectedNodes.Clear();

				selectedNodes.AddRange(newSelected);

				OnSelectionChanged();
			}

			Invalidate();
		}

		private void ClearSelection()
		{
			selectedNodes.ForEach(h => h.Node.ClearSelection());

			selectedNodes.Clear();
		}

		private void RemoveSelectedNodes()
		{
			selectedNodes.Where(h => !(h.Node is ClassNode)).ForEach(h => h.Node.ParentNode.RemoveNode(h.Node));

			selectedNodes.Clear();

			OnSelectionChanged();

			Invalidate();
		}

		private void CopySelectedNodesToClipboard()
		{
			if (selectedNodes.Count > 0)
			{
				ReClassClipboard.CopyNodes(selectedNodes.Select(h => h.Node), ClassManager.Classes, Program.Logger);
			}
		}

		private void PasteNodeFromClipboardToSelection()
		{
			if (selectedNodes.Count == 1)
			{
				var selectedNode = selectedNodes.First().Node;
				var parent = selectedNode.ParentNode as ClassNode;
				if (parent != null)
				{
					var nodes = ReClassClipboard.PasteNodes(parent, ClassManager.Classes, Program.Logger);
					foreach (var node in nodes)
					{
						try
						{
							parent.InsertNode(selectedNode, node);
						}
						catch (ClassCycleException)
						{
							MessageBox.Show("Can't paste node because this would create a cycle.");
						}
					}
				}
			}
		}
	}
}
