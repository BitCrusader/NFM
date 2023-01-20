﻿using System;
using System.Collections.Generic;
using Avalonia.Rendering;
using NFM.GPU;
using NFM.World;
using Vortice.Direct3D12;

namespace NFM.Graphics;

public static class Renderer
{
	/// <summary>
	/// "Shared" command list, guaranteed to be executed just before any frames are rendered.
	/// </summary>
	public static CommandList DefaultCommandList { get; private set; } = new CommandList();

	private static List<SceneStep> sceneSteps= new();

	public static void AddStep(SceneStep step)
	{
		sceneSteps.Add(step);
		step.Init();
	}

	public static void Init()
	{
		D3DContext.Init(2);
		DefaultCommandList.Name = "Default List";
		DefaultCommandList.Open();

		AddStep(new SkinningStep());
	}

	public static void RenderFrame()
	{
		// Run scene render steps.
		DefaultCommandList.BeginEvent("Update scenes");
		foreach (Scene scene in Scene.All)
		{
			// Execute per-scene render steps.
			foreach (var step in sceneSteps)
			{
				step.Scene = scene;

				step.List.BeginEvent($"{step.GetType().Name} (scene)");
				step.Run();
				step.List.EndEvent();
			}
		}

		// We don't want other threads submitting uploads while the list is closed.
		lock (DefaultCommandList)
		{
			// Execute default command list and wait for it on the GPU.
			DefaultCommandList.EndEvent();
			DefaultCommandList.Close();
			DefaultCommandList.Execute();

			// Render to each viewport.
			foreach (var viewport in Viewport.All)
			{
				RenderCamera(viewport.Camera, viewport.Swapchain);
			}

			// Wait for completion.
			D3DContext.WaitFrame();

			// Reopen default command list
			DefaultCommandList.Open();
		}
	}

	public static void RenderCamera(CameraNode camera, Swapchain swapchain)
	{
		RenderCamera(camera, swapchain.RT, (o) => o.RequestState(swapchain.RT, ResourceStates.Present));
		swapchain.Present();
	}
	
	public static void RenderCamera(CameraNode camera, Texture texture) => RenderCamera(camera, texture, null);
	private static void RenderCamera(CameraNode camera, Texture texture, Action<CommandList> beforeExecute)
	{
		// Grab an RP instance and open it's command list
		var rp = StandardRenderPipeline.Get(texture);
		rp.List.Open();

		// Execute the render pipeline
		rp.Render(texture, camera);
		beforeExecute?.Invoke(rp.List);

		// Close/execute the command list
		rp.List.Close();
		rp.List.Execute();
	}

	public static void Cleanup()
	{
		D3DContext.Flush();
	}
}