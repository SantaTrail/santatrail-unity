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
namespace Esri.GameEngine.RCQ
{
    /// <summary>
    /// Instance buffer types.
    /// </summary>
    public enum ArcGISInstanceBufferType
    {
        /// <summary>
        /// Instance buffer.
        /// </summary>
        Instance = 0,
        
        /// <summary>
        /// Instance flags buffer containing a uint32 with the first bit encoded
        /// to notify if the feature is selected or not. This first bit is currently
        /// not used for Game Engine clients. Second and third bits encode the color mode mix.
        /// </summary>
        InstanceFlag = 1
    };
}