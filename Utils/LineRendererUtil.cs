using System.Data;
using LethalBots.Constants;
using UnityEngine;

namespace LethalBots.Utils
{
    /// <summary>
    /// Class that use an list of linerenderers to return to the user of the class, for drawing things
    /// </summary>
    public class LineRendererUtil
    {
        private LineRenderer[] _listLineRenderers;
        private Transform _transformToParent;
        private int _index;

        public LineRendererUtil(int nbMaxLineRenderer, Transform transformToParent)
        {
            this._listLineRenderers = new LineRenderer[nbMaxLineRenderer];
            this._transformToParent = transformToParent;
        }

        public LineRenderer? GetLineRenderer()
        {
            if (!DebugConst.DRAW_LINES)
            {
                return null;
            }

#pragma warning disable CS0162
            for (int i = 0; i < _listLineRenderers.Length; i++)
            {
                if (_listLineRenderers[i] == null)
                {
                    _listLineRenderers[i] = CreateLineRenderer();
                    return _listLineRenderers[i];
                }
            }
#pragma warning restore CS0162

            if (_index >= _listLineRenderers.Length)
            {
                _index = 0;
            }
            return _listLineRenderers[_index++];
        }

        private LineRenderer CreateLineRenderer()
        {
            LineRenderer lineRenderer = new GameObject().AddComponent<LineRenderer>();
            return InitLineRenderer(ref lineRenderer);
        }

        private LineRenderer InitLineRenderer(ref LineRenderer lineRenderer)
        {
            lineRenderer.gameObject.transform.SetParent(_transformToParent.transform, false);
            lineRenderer.gameObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return lineRenderer;
        }
    }
}
