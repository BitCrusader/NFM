﻿using System;
using System.Collections;

namespace NFM.World;

public partial class Scene : IDisposable
{
	public static List<Scene> All { get; } = new();

	[Notify, Save] public static Scene Main { get; set; } = new();

	[Notify] public ReadOnlyObservableCollection<Node> RootNodes { get; }
	[Save] private ObservableCollection<Node> rootNodes { get; set; } = new();

	public Scene()
	{
		RootNodes = new(rootNodes);
		All.Add(this);

		TransformBuffer.Name = "Transform Buffer";
		InstanceBuffer.Name = "Instance Buffer";
	}

	/// <summary>
	/// Adds a Node as a scene root. Should NEVER be called manually.
	/// </summary>
	internal void AddRootNode(Node node)
	{
		rootNodes.Add(node);
	}

	/// <summary>
	/// Removes a Node as a scene root. Should NEVER be called manually.
	/// </summary>
	internal bool RemoveRootNode(Node node)
	{
		return rootNodes.Remove(node);
	}

	public void Dispose()
	{
		for (int i = rootNodes.Count - 1; i >= 0; i--)
		{
			rootNodes[i].Dispose();
		}

		InstanceBuffer.Dispose();
		TransformBuffer.Dispose();

		All.Remove(this);
	}
}
