//
// Copyright (c) 2017 Geri Borbás http://www.twitter.com/_eppz
//
//  Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//  The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
using UnityEngine;
using System.Collections;
using EPPZ.Geometry.Model;
using EPPZ.Geometry.Inspector;

namespace EPPZ.Geometry.Scenes
{

	using Model;
	using Inspector;


	/// <summary>
	/// 9. Polygon offset
	/// </summary>
	public class Controller_9 : MonoBehaviour
	{


		[Range(0, 2)] public float offset = 0.2f;

		public Source.Polygon polygonSource;
		//public PolygonLineRenderer offsetPolygonRenderer;

		private Polygon offsetPolygon;
		private Polygon polygon { get { return polygonSource.polygon; } }

		public PolygonInspector polygonInspector;
		public PolygonInspector offsetPolygonInspector;


		void Start()
		{
			// Debug.
			polygonInspector.polygon = polygonSource.polygon;
		}

		void Update()
		{
			offsetPolygon = polygon.OffsetPolygon(offset);

			// Render.
			//offsetPolygonRenderer.polygon = offsetPolygon;

			// Debug.
			offsetPolygonInspector.polygon = offsetPolygon;
		}
	}
}


