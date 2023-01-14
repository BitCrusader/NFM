﻿using System;
using System.Reactive.Linq;
using NFM.Editor;
using NFM.Rendering;
using ReactiveUI;

namespace NFM.World
{
	[Icon('\uE3C2')]
	public class Node : ISelectable, IDisposable
	{
		[Inspect] public string Name { get; set; }

		[Inspect] public Vector3 Position { get; set; } = Vector3.Zero;
		[Inspect] public Vector3 Rotation { get; set; } = Vector3.Zero;
		[Inspect] public Vector3 Scale { get; set; } = Vector3.One;

		[Notify] public Matrix4 Transform { get; private set; } = Matrix4.Identity;

		public Scene Scene { get; }

		public ReadOnlyObservableCollection<Node> Children { get; }
		private ObservableCollection<Node> children = new();

		private Node parent;
		public Node Parent
		{
			get => parent;
			set
			{
				Debug.Assert(value != this, "Nodes cannot be parented to themselves.");
				Debug.Assert(value == null || value.Scene == Scene,
					"Nodes can only be parented to other nodes from the same scene.");

				if (parent != value || value == null /*Could be initial setup...*/)
				{
					if (parent == null)
					{
						Scene.RemoveRootNode(this);
					}
					if (value == null)
					{
						Scene.AddRootNode(this);
					}

					parent?.children.Remove(this);
					parent = value;
					parent?.children.Add(this);
				}
			}
		}

		public Node(Scene scene)
		{
			string name = GetType().Name.PascalToDisplay();
			if (name.EndsWith(" Node"))
			{
				name = name.Remove(name.Length - " Node".Length);
			}

			Name = name;
			Scene = scene ?? Scene.Main;
			Parent = null;
			Children = new(children);

			// Track changes in display transform
			this.WhenAnyValue(o => o.Position, o => o.Rotation, o => o.Scale)
				.Subscribe(o => UpdateTransform());
		}

		void UpdateTransform()
		{
			// Grab base transform.
			Matrix4 result = Matrix4.CreateTransform(Position, Rotation, Scale);

			// Apply parent transforms.
			if (parent != null)
			{
				result *= parent.Transform;
			}

			Transform = result;

			// Recursively update children.
			foreach (var child in Children)
			{
				child.UpdateTransform();
			}
		}

		public virtual void Dispose()
		{
			// Make sure we're not still selected.
			Selection.Deselect(this);

			// Remove self from scene tree.
			Parent = null;

			foreach (var child in children)
			{
				child.Dispose();
			}
		}

		public virtual void DrawGizmos(GizmosContext context) {}
	}
}