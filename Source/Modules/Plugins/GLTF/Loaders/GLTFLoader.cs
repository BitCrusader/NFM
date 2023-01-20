﻿using System;
using System.Collections;
using System.Runtime.InteropServices;
using NFM;
using NFM.Resources;
using NFM.Mathematics;
using Asset = NFM.Resources.Asset;
using ModelPart = NFM.Resources.ModelPart;
using Material = NFM.Resources.Material;
using Mesh = NFM.Resources.Mesh;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;
using StbiSharp;
using NFM.Common;
using SharpGLTF.IO;

namespace GLTF.Loaders
{
	public class GLTFLoader : ResourceLoader<Model>
	{
		public string Path;

		public GLTFLoader(string path)
		{
			Path = path;
		}

		public override async Task<Model> Load()
		{
			// Load GLTF model from file.
			ModelRoot model = ModelRoot.Load(Path, ValidationMode.Skip);

			// Load textures from GLTF
			Texture2D[] gameTextures = new Texture2D[model.LogicalTextures.Count];
			Parallel.ForEach(model.LogicalTextures, (texture, ct) =>
			{
				using (StbiImage image = Stbi.LoadFromMemory(texture.PrimaryImage.Content.Content.Span, 4))
				{
					Texture2D gameTexture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA8, 4);
					gameTexture.SetPixelData(ToReadWriteSpan(image.Data), 0, true);

					gameTextures[texture.LogicalIndex] = gameTexture;
				}
			});

			// Load materials from GLTF
			Material[] gameMaterials = new Material[model.LogicalMaterials.Count];
			for (int i = 0; i < model.LogicalMaterials.Count; i++)
			{
				var material = model.LogicalMaterials[i];

				// Check if this material uses KHR_materials_transmission
				bool useTransmission = material.FindChannel("Transmission") != null;

				// Determine shader and create material.
				Shader shader = material.Alpha switch
				{
					AlphaMode.MASK => await Asset.LoadAsync<Shader>("USER:/Shaders/Transparent.hlsl"),
					AlphaMode.BLEND => await Asset.LoadAsync<Shader>("USER:/Shaders/Transparent.hlsl"),
					_ => await Asset.LoadAsync<Shader>("USER:/Shaders/Opaque.hlsl")
				};
				Material gameMaterial = new Material(shader);

				// Loop over material channels
				foreach (var channel in material.Channels)
				{
					if (channel.Key == "BaseColor" && channel.Texture != null)
					{
						gameMaterial.SetTexture("BaseColor", gameTextures[channel.Texture.LogicalIndex]);
					}
					else if (channel.Key == "Normal" && channel.Texture != null)
					{
						gameMaterial.SetTexture("Normal", gameTextures[channel.Texture.LogicalIndex]);
					}
					else if (channel.Key == "MetallicRoughness" && channel.Texture != null)
					{
						gameMaterial.SetTexture("ORM", gameTextures[channel.Texture.LogicalIndex]);
					}
				}

				gameMaterials[i] = gameMaterial;
			}
			
			Debug.Assert(model.LogicalSkins.Count <= 1, "GLTF models with multiple skeletons are not supported");

			// Build skeleton.
			Bone rootBone = null;
			if (model.LogicalSkins.Count > 0)
			{
				var skin = model.LogicalSkins[0];

				// Find root node (joint with parent outside the skeleton).
				Node root = skin.Skeleton;
				if (root == null)
				{
					for (int i = 0; i < skin.JointsCount; i++)
					{
						var joint = skin.GetJoint(i);
						if (!joint.Joint.VisualParent.IsSkinJoint)
						{
							root = joint.Joint;
							break;
						}
					}
				}

				// Collect joint info.
				List<(Node, Matrix4)> joints = new(skin.JointsCount);
				for (int i = 0; i < skin.JointsCount; i++)
				{
					var joint = skin.GetJoint(i);
					joints.Add((joint.Joint, (Matrix4)joint.InverseBindMatrix));
				}

				// Build the skeleton.
				rootBone = BuildSkeleton(root, joints);
			}

			// Build model parts (one per GLTF mesh).
			var parts = new List<ModelPart>(model.LogicalMeshes.Count);
			foreach (var node in model.LogicalNodes.Where(o => o.Mesh != null))
			{
				var mesh = node.Mesh;

				Mesh[] meshes = new Mesh[mesh.Primitives.Count];
				Parallel.ForEach(mesh.Primitives, (primitive) =>
				{
					// Get node transform as Z-up.
					var worldMatrix = (Matrix4)node.WorldMatrix;
					worldMatrix *= Matrix4.CreateRotation(new(90, 0, 0));

					// Build vertices for the base mesh.
					var baseVertices = BuildVertices(primitive.VertexAccessors, worldMatrix);

					// Create mesh and add to collection.
					var gameMesh = new Mesh();
					gameMesh.SetVertices(baseVertices);
					gameMesh.SetIndices(primitive.GetIndices().ToArray());
					gameMesh.SetMaterial(gameMaterials[primitive.Material.LogicalIndex]);
					gameMesh.Commit();

					meshes[primitive.LogicalIndex] = gameMesh;
				});

				parts.Add(new ModelPart(meshes));
			}

			return new Model(parts.ToArray())
			{
				Skeleton = rootBone
			};
		}

		private Bone BuildSkeleton(Node node, List<(Node, Matrix4)> joints)
		{
			// Collect joint info.
			var boneTransform = (Matrix4)node.WorldMatrix * Matrix4.CreateRotation(new(90, 0, 0));
			var parentTransform = (Matrix4)node.VisualParent.WorldMatrix  * Matrix4.CreateRotation(new(90, 0, 0));

			// Build child bones.
			var children = node.VisualChildren
				.Select(o => BuildSkeleton(o, joints));

			// Find bone ID.
			int boneID = joints.FindIndex(o => o.Item1 == node);

			return new Bone(node.Name, boneID, boneTransform, parentTransform, children);
		}

		private Vertex[] BuildVertices(IReadOnlyDictionary<string, Accessor> accessors, Matrix4 transform)
		{
			Debug.Assert(accessors.ContainsKey("POSITION"));

			var positions = accessors.GetValueOrDefault("POSITION").AsSpan<Vector3>();
			var normals = accessors.GetValueOrDefault("NORMAL").AsSpan<Vector3>();
			var uv0 = accessors.GetValueOrDefault("TEXCOORD_0").AsSpan<Vector2>();
			var uv1 = accessors.GetValueOrDefault("TEXCOORD_1").AsSpan<Vector2>();

			Vertex[] result = new Vertex[positions.Length];
			for (int i = 0; i < result.Length; i++)
			{
				Vertex vertex = new Vertex();
				vertex.Position = (new Vector4(positions[i].X, positions[i].Y, positions[i].Z, 1) * transform).Xyz;

				if (normals != null)
				{
					vertex.Normal = (new Vector4(normals[i].X, normals[i].Y, normals[i].Z, 1) * transform).Xyz;
				}
				if (uv0 != null)
				{
					vertex.UV0 = new Vector2(uv0[i].X, uv0[i].Y);
				}
				if (uv1 != null)
				{
					vertex.UV1 = new Vector2(uv1[i].X, uv1[i].Y);
				}

				result[i] = vertex;
			}

			return result;
		}

		private unsafe Span<T> ToReadWriteSpan<T>(ReadOnlySpan<T> source) where T : unmanaged
		{
			fixed (T* dataPtr = source)
			{
				return new Span<T>(dataPtr, source.Length);
			}
		}
	}

	public static class GLTFHelpers
	{
		public static unsafe Span<T> AsSpan<T>(this Accessor accessor) where T : unmanaged
		{
			if (accessor == null)
			{
				return null;
			}

			var slice = accessor.SourceBufferView.Content.Slice(accessor.ByteOffset, accessor.ByteLength);
			return MemoryMarshal.Cast<byte, T>(slice);
		}

		public static bool TryGetNode(this JsonContent source, string name, out JsonContent result)
		{
			try
			{
				result = source.GetNode(name);
				return true;
			}
			catch
			{
				result = default;
				return false;
			}
		}
	}
}
