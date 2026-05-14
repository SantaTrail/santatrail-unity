// COPYRIGHT 1995-2022 ESRI
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
using Esri.ArcGISMapsSDK.Renderer.GPUResources;
using Esri.GameEngine.RCQ;
using Esri.HPFramework;
using Unity.Mathematics;
using UnityEngine;

namespace Esri.ArcGISMapsSDK.Renderer.Renderables
{
	internal class Renderable : IRenderable
	{
		public GameObject RenderableGameObject { get; }

		private ArcGISRenderableType renderableType = ArcGISRenderableType.SceneNode;
		public ArcGISRenderableType RenderableType
		{
			get => renderableType;
			set => renderableType = value;
		}

		private IGPUResourceMaterial material;
		public IGPUResourceMaterial Material
		{
			get => material;
			set
			{
				material = value;

				if (renderableType == ArcGISRenderableType.PointCloud)
				{
					var instancedMeshRenderer = RenderableGameObject.GetComponent<PointCloudRenderer>();

					instancedMeshRenderer.Material = material?.NativeMaterial;
				}
				else if (renderableType == ArcGISRenderableType.InstancedMesh)
				{
					var instancedMeshRenderer = RenderableGameObject.GetComponent<PointSceneRenderer>();

					instancedMeshRenderer.Material = material?.NativeMaterial;
				}
				else
				{
					RenderableGameObject.GetComponent<MeshRenderer>().material = material?.NativeMaterial;
				}
			}
		}

		private IGPUResourceMesh mesh;
		public IGPUResourceMesh Mesh
		{
			get => mesh;
			set
			{
				mesh = value;

				if (renderableType == ArcGISRenderableType.PointCloud)
				{
					var instancedBillboardRenderer = RenderableGameObject.GetComponent<PointCloudRenderer>();

					instancedBillboardRenderer.Mesh = mesh?.NativeMesh;
				}
				else if (renderableType == ArcGISRenderableType.InstancedMesh)
				{
					var instancedMeshRenderer = RenderableGameObject.GetComponent<PointSceneRenderer>();

					instancedMeshRenderer.Mesh = mesh?.NativeMesh;
				}
				else
				{
					var meshFilter = RenderableGameObject.GetComponent<MeshFilter>();
					var collisionComponent = RenderableGameObject.GetComponent<MeshCollider>();

					meshFilter.sharedMesh = mesh?.NativeMesh;
					collisionComponent.sharedMesh = collisionComponent.enabled ? mesh?.NativeMesh : null;
				}
			}
		}

		private IGPUResourceInstanceBuffer instanceBuffer;
		public IGPUResourceInstanceBuffer InstanceBuffer
		{
			get => instanceBuffer;
			set
			{
				instanceBuffer = value;

				if (renderableType == ArcGISRenderableType.PointCloud)
				{
					var instancedMeshRenderer = RenderableGameObject.GetComponent<PointCloudRenderer>();

					instancedMeshRenderer.InstanceBuffer = instanceBuffer?.NativeBuffer;
				}
			}
		}

		double3 pivot;
		public double3 Pivot
		{
			get
			{
				return pivot;
			}
			set
			{
				pivot = value;

				var hpTransform = RenderableGameObject.GetComponent<HPTransform>();

				hpTransform.UniversePosition = value;
				hpTransform.UniverseRotation = Quaternion.identity;
				hpTransform.LocalScale = Vector3.one;
			}
		}

		public string Name
		{
			get
			{
				return RenderableGameObject.name;
			}

			set
			{
				RenderableGameObject.name = value;
			}
		}

		public bool IsVisible
		{
			get
			{
				return RenderableGameObject.activeInHierarchy;
			}

			set
			{
				RenderableGameObject.SetActive(value);
			}
		}

		public bool IsMeshColliderEnabled
		{
			get
			{
				return RenderableGameObject.GetComponent<MeshCollider>();
			}

			set
			{
				if (RenderableType != ArcGISRenderableType.InstancedMesh &&
					RenderableType != ArcGISRenderableType.PointCloud)
				{
					var component = RenderableGameObject.GetComponent<MeshCollider>();
					component.enabled = value;

					if (value)
					{
						component.sharedMesh = RenderableGameObject.GetComponent<MeshFilter>().sharedMesh;
					}
				}
			}
		}

		public uint LayerId { get; set; } = 0;

		private Matrix4x4 localMatrix { get; set; }
		public Matrix4x4 LocalMatrix
		{
			get
			{
				return localMatrix;
			}
			set
			{
				localMatrix = value;

				if (renderableType == ArcGISRenderableType.InstancedMesh)
				{
					var instancedRenderer = RenderableGameObject.GetComponent<PointSceneRenderer>();
					instancedRenderer.LocalMatrix = localMatrix;
				}
			}
		}

		private OrientedBoundingBox orientedBoundingBox;
		public OrientedBoundingBox OrientedBoundingBox
		{
			get
			{
				return orientedBoundingBox;
			}
			set
			{
				orientedBoundingBox = value;

				if (renderableType == ArcGISRenderableType.PointCloud)
				{
					var instancedRenderer = RenderableGameObject.GetComponent<PointCloudRenderer>();

					instancedRenderer.OrientedBoundingBox = orientedBoundingBox;
				}
			}
		}

		public bool MaskTerrain { get; set; }

		public Renderable(GameObject gameObject)
		{
			RenderableGameObject = gameObject;
		}

		public void SetInstanceData(IGPUResourcesProvider gpuResourcesProvider, ArcGISInstanceBufferType instanceBufferType, ArcGISDataBufferView instances)
		{
			if (RenderableType == ArcGISRenderableType.PointCloud && instanceBufferType == ArcGISInstanceBufferType.Instance)
			{
				var instanceData = instances.ToNativeArray<PointCloudInstanceData>();
				InstanceBuffer = gpuResourcesProvider.CreateInstanceBuffer(instanceData);
			}
			else if (RenderableType == ArcGISRenderableType.InstancedMesh)
			{
				var instancedMeshRenderer = RenderableGameObject.GetComponent<PointSceneRenderer>();
				if (instancedMeshRenderer != null)
				{
					if (instanceBufferType == ArcGISInstanceBufferType.Instance)
					{
						var instanceData = instances.ToNativeArray<FeatureInstanceData>();
						instancedMeshRenderer.AddInstanceData(instanceData);
					}
					else if (instanceBufferType == ArcGISInstanceBufferType.InstanceFlag)
					{
						var instancesData = instances.ToNativeArray<uint>();
						instancedMeshRenderer.AddInstanceFlagData(instancesData);
					}
				}
			}
		}

		public void Destroy()
		{
			if (Application.isEditor)
			{
				Object.DestroyImmediate(RenderableGameObject);
			}
			else
			{
				Object.Destroy(RenderableGameObject);
			}
		}
	}
}
