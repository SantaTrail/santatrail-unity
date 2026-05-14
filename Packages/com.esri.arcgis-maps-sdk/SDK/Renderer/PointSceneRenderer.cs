// COPYRIGHT 1995-2026 ESRI
// TRADE SECRETS: ESRI PROPRIETARY AND CONFIDENTIAL
// Unpublished material - all rights reserved under the
// Copyright Laws of the United States and applicable international
// laws, treaties, and conventions.
//
// For additional information, contact:
// Attn: Contracts and Legal Department
// Environmental Systems Research Institute, Inc.
// 380 New York Street
// Redlands, California 92373
// USA
//
// email: legal@esri.com
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Esri.ArcGISMapsSDK.Renderer
{
	[ExecuteAlways]
	internal class PointSceneRenderer : MonoBehaviour
	{
		MaterialPropertyBlock materialPropertyBlock;

		public Material Material { get; set; }

		public Mesh Mesh { get; set; }

		private Matrix4x4[] instanceTransforms;

		private Matrix4x4 localMatrix;
		public Matrix4x4 LocalMatrix
		{
			get => localMatrix;
			set
			{
				localMatrix = value;
				isDirty = true;
			}
		}

		private Texture2D colorsTexture;
		private Texture2D flagsTexture;

		private bool isDirty = false;

		private void LateUpdate()
		{
			EnsureInitialization();

			if (instanceTransforms.Length <= 0 || Material == null)
			{
				return;
			}

			if (isDirty)
			{
				for (var i = 0; i < instanceTransforms.Length; ++i)
				{
					instanceTransforms[i] = transform.localToWorldMatrix * instanceTransforms[i] * localMatrix;
				}

				isDirty = false;
			}

			materialPropertyBlock.SetMatrix("_WorldToBatchRootMatrix", transform.worldToLocalMatrix);

			var renderParams = new RenderParams(Material)
			{
				matProps = materialPropertyBlock,
				shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On
			};

			const int maxInstancesPerDrawCall = 500;
			var totalInstances = instanceTransforms.Length;
			var numDrawCalls = Mathf.CeilToInt((float)totalInstances / maxInstancesPerDrawCall);

			for (var j = 0; j < numDrawCalls; ++j)
			{
				var startIndex = j * maxInstancesPerDrawCall;
				var count = Mathf.Min(maxInstancesPerDrawCall, totalInstances - startIndex);

				var slice = new Matrix4x4[count];
				Array.Copy(instanceTransforms, startIndex, slice, 0, count);

				// Max 1023 at once if not passing worldToObject matrix (which is used by default): https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Graphics.RenderMeshInstanced.html
				// If not deactivated (can be done in .hlsl), max instances is 511.
				Graphics.RenderMeshInstanced(renderParams, Mesh, 0, slice);
			}
		}

		public void AddInstanceData<T>(NativeArray<T> instanceData) where T : struct
		{
			EnsureInitialization();

			var pointData = instanceData.Reinterpret<FeatureInstanceData>(UnsafeUtility.SizeOf<FeatureInstanceData>());
			Debug.Assert(pointData != null);

			colorsTexture = new Texture2D(pointData.Length, 1, TextureFormat.RGBA32, false, true)
			{
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
			};

			instanceTransforms = new Matrix4x4[pointData.Length];

			for (var i = 0; i < pointData.Length; i++)
			{
				var m = pointData[i].transform;

				Vector3 position = m.GetColumn(3);

				var oldScale = new Vector3(
					new Vector3(m.m00, m.m10, m.m20).magnitude,
					new Vector3(m.m01, m.m11, m.m21).magnitude,
					new Vector3(m.m02, m.m12, m.m22).magnitude
				);

				// Remove scale from rotation columns.
				var up = new Vector3(m.m01, m.m11, m.m21) / oldScale.y;
				var forward = new Vector3(m.m02, m.m12, m.m22) / oldScale.z;

				var rotation = Quaternion.LookRotation(forward, up);
				var newScale = pointData[i].scale;

				instanceTransforms[i] = Matrix4x4.TRS(position, rotation, newScale);

				var color = pointData[i].color;
				var a = ((color & 0xff000000) >> 24) / 255.0f;
				var b = ((color & 0x00ff0000) >> 16) / 255.0f;
				var g = ((color & 0x0000ff00) >> 8) / 255.0f;
				var r = ((color & 0x000000ff) >> 0) / 255.0f;

				colorsTexture.SetPixel(i, 0, new Color(r, g, b, a));
			}

			colorsTexture.Apply();

			materialPropertyBlock.SetTexture("_Colors", colorsTexture);
			materialPropertyBlock.SetFloat("_ColorsLength", pointData.Length);

			isDirty = true;
		}

		public void AddInstanceFlagData<T>(NativeArray<T> data) where T : struct
		{
			EnsureInitialization();

			var flagData = data.Reinterpret<uint>();

			flagsTexture = new Texture2D(flagData.Length, 1, TextureFormat.R8, false, true)
			{
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp
			};

			for (var i = 0; i < flagData.Length; i++)
			{
				var flag = (flagData[i] & 6u) >> 1;
				flagsTexture.SetPixel(i, 0, new Color(flag, 0.0f, 0.0f));
			}

			flagsTexture.Apply();

			materialPropertyBlock.SetTexture("_Flags", flagsTexture);
		}

		private void EnsureInitialization()
		{
			materialPropertyBlock ??= new MaterialPropertyBlock();
		}
	}
}
