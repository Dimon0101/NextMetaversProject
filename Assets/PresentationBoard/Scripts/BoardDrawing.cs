using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace NextMetavers
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class BoardDrawing : UdonSharpBehaviour
    {
        private const int MaxStrokes = 120;
        private const int MaxPoints = 1500;
        private const int ToolBrush = 0;
        private const int ToolLine = 1;
        private const int ToolRect = 2;
        private const int ToolEllipse = 3;
        private const int ToolArrow = 4;
        private const int ToolEraser = 5;
        private const float MinStep = 0.015f;
        private const float SyncInterval = 0.4f;
        private const float RayLength = 15f;

        public PresentationBoard board;
        public Camera drawCamera;
        public Transform[] surfaces;
        public LineRenderer[] strokeLines;
        public Color[] palette;
        public float[] widths;
        public Vector2 areaSize = new Vector2(4.6f, 2.45f);
        public int surfaceMask = 1 << 17;
        public Text[] toolTexts;
        public Vector2 inputPoint;

        [UdonSynced] private int[] _syncColor = new int[0];
        [UdonSynced] private int[] _syncWidth = new int[0];
        [UdonSynced] private int[] _syncCount = new int[0];
        [UdonSynced] private float[] _syncX = new float[0];
        [UdonSynced] private float[] _syncY = new float[0];

        private int[] _color = new int[MaxStrokes];
        private int[] _width = new int[MaxStrokes];
        private int[] _count = new int[MaxStrokes];
        private float[] _pointX = new float[MaxPoints];
        private float[] _pointY = new float[MaxPoints];
        private int _strokes;
        private int _points;

        private int _tool = ToolBrush;
        private int _colorIndex;
        private int _widthIndex = 1;
        private bool _drawing;
        private int _activeStroke = -1;
        private int _activeStart;
        private Vector2 _startPoint;
        private Vector2 _lastPoint;
        private float _nextSync;
        private int _renderFrames;

        private void Start()
        {
            Rebuild();
            UpdateToolText();
        }

        public override void OnDeserialization()
        {
            _strokes = _syncCount.Length;
            _points = 0;
            for (int i = 0; i < _strokes && i < MaxStrokes; i++)
            {
                _color[i] = _syncColor[i];
                _width[i] = _syncWidth[i];
                _count[i] = _syncCount[i];
                _points += _syncCount[i];
            }

            for (int i = 0; i < _points && i < MaxPoints; i++)
            {
                _pointX[i] = _syncX[i];
                _pointY[i] = _syncY[i];
            }
            Rebuild();
        }

        public override void InputUse(bool value, UdonInputEventArgs args)
        {
            if (value)
            {
                if (!CanDraw() || !TryGetPoint()) return;

                _drawing = true;
                _StrokeBegin();
                return;
            }

            if (!_drawing) return;

            _drawing = false;
            _StrokeEnd();
        }

        private void Update()
        {
            if (_renderFrames > 0)
            {
                _renderFrames--;
                if (_renderFrames == 0 && drawCamera != null) drawCamera.enabled = false;
            }

            if (!_drawing) return;
            if (!TryGetPoint()) return;

            _StrokeMove();
        }

        public void _StrokeBegin()
        {
            if (!CanDraw()) return;

            _startPoint = ToArea(inputPoint);
            _lastPoint = _startPoint;

            if (_tool == ToolEraser)
            {
                EraseAt(_startPoint);
                return;
            }

            TakeOwnership();
            if (!BeginStroke()) return;

            AddPoint(_startPoint);
            AddPoint(_startPoint);
            Rebuild();
            _nextSync = Time.timeSinceLevelLoad + SyncInterval;
        }

        public void _StrokeMove()
        {
            if (!CanDraw()) return;

            Vector2 point = ToArea(inputPoint);

            if (_tool == ToolEraser)
            {
                EraseAt(point);
                return;
            }

            if (_activeStroke < 0) return;

            if (_tool == ToolBrush)
            {
                if ((point - _lastPoint).magnitude < MinStep) return;

                if (!AddPoint(point)) return;
                _lastPoint = point;
            }
            else
            {
                _points = _activeStart;
                _count[_activeStroke] = 0;
                BuildShape(_startPoint, point);
                _lastPoint = point;
            }

            Rebuild();
            if (Time.timeSinceLevelLoad >= _nextSync)
            {
                _nextSync = Time.timeSinceLevelLoad + SyncInterval;
                PushSync();
            }
        }

        public void _StrokeEnd()
        {
            if (_activeStroke < 0)
            {
                if (_tool == ToolEraser) PushSync();
                return;
            }

            if (_count[_activeStroke] < 2)
            {
                _points = _activeStart;
                _strokes--;
            }

            _activeStroke = -1;
            Rebuild();
            PushSync();
        }

        public void ToolBrushMode()
        {
            _tool = ToolBrush;
            UpdateToolText();
        }

        public void ToolLineMode()
        {
            _tool = ToolLine;
            UpdateToolText();
        }

        public void ToolRectMode()
        {
            _tool = ToolRect;
            UpdateToolText();
        }

        public void ToolEllipseMode()
        {
            _tool = ToolEllipse;
            UpdateToolText();
        }

        public void ToolArrowMode()
        {
            _tool = ToolArrow;
            UpdateToolText();
        }

        public void ToolEraserMode()
        {
            _tool = ToolEraser;
            UpdateToolText();
        }

        public void WidthThin()
        {
            _widthIndex = 0;
            UpdateToolText();
        }

        public void WidthMedium()
        {
            _widthIndex = 1;
            UpdateToolText();
        }

        public void WidthThick()
        {
            _widthIndex = 2;
            UpdateToolText();
        }

        public void Color0()
        {
            SetColor(0);
        }

        public void Color1()
        {
            SetColor(1);
        }

        public void Color2()
        {
            SetColor(2);
        }

        public void Color3()
        {
            SetColor(3);
        }

        public void Color4()
        {
            SetColor(4);
        }

        public void Color5()
        {
            SetColor(5);
        }

        public void Undo()
        {
            if (!CanDraw() || _strokes == 0) return;

            TakeOwnership();
            _strokes--;
            _points -= _count[_strokes];
            _activeStroke = -1;
            Rebuild();
            PushSync();
        }

        public void ClearAll()
        {
            if (!CanDraw()) return;

            TakeOwnership();
            _strokes = 0;
            _points = 0;
            _activeStroke = -1;
            Rebuild();
            PushSync();
        }

        private void SetColor(int index)
        {
            _colorIndex = Mathf.Clamp(index, 0, palette.Length - 1);
            UpdateToolText();
        }

        private bool CanDraw()
        {
            return board == null || board.HasDrawRights();
        }

        private bool TryGetPoint()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return false;

            VRCPlayerApi.TrackingDataType source = local.IsUserInVR()
                ? VRCPlayerApi.TrackingDataType.RightHand
                : VRCPlayerApi.TrackingDataType.Head;
            VRCPlayerApi.TrackingData tracking = local.GetTrackingData(source);

            RaycastHit hit;
            if (!Physics.Raycast(tracking.position, tracking.rotation * Vector3.forward, out hit, RayLength, surfaceMask)) return false;

            Transform surface = hit.collider.transform;
            for (int i = 0; i < surfaces.Length; i++)
            {
                if (surfaces[i] != surface) continue;

                Vector3 local3 = surface.InverseTransformPoint(hit.point);
                inputPoint = new Vector2(Mathf.Clamp(local3.x, -0.5f, 0.5f), Mathf.Clamp(local3.y, -0.5f, 0.5f));
                return true;
            }
            return false;
        }

        private Vector2 ToArea(Vector2 normalized)
        {
            return new Vector2(normalized.x * areaSize.x, normalized.y * areaSize.y);
        }

        private bool BeginStroke()
        {
            while (_strokes >= MaxStrokes || _points + 64 > MaxPoints)
            {
                if (_strokes == 0) return false;
                DropOldest();
            }

            _color[_strokes] = _colorIndex;
            _width[_strokes] = _widthIndex;
            _count[_strokes] = 0;
            _activeStroke = _strokes;
            _activeStart = _points;
            _strokes++;
            return true;
        }

        private void DropOldest()
        {
            int first = _count[0];
            for (int i = 0; i < _points - first; i++)
            {
                _pointX[i] = _pointX[i + first];
                _pointY[i] = _pointY[i + first];
            }
            for (int i = 0; i < _strokes - 1; i++)
            {
                _color[i] = _color[i + 1];
                _width[i] = _width[i + 1];
                _count[i] = _count[i + 1];
            }
            _points -= first;
            _strokes--;
            if (_activeStroke > 0) _activeStroke--;
            _activeStart -= first;
        }

        private bool AddPoint(Vector2 point)
        {
            if (_points >= MaxPoints || _activeStroke < 0) return false;

            _pointX[_points] = point.x;
            _pointY[_points] = point.y;
            _points++;
            _count[_activeStroke]++;
            return true;
        }

        private void BuildShape(Vector2 from, Vector2 to)
        {
            if (_tool == ToolLine)
            {
                AddPoint(from);
                AddPoint(to);
                return;
            }

            if (_tool == ToolRect)
            {
                AddPoint(from);
                AddPoint(new Vector2(to.x, from.y));
                AddPoint(to);
                AddPoint(new Vector2(from.x, to.y));
                AddPoint(from);
                return;
            }

            if (_tool == ToolEllipse)
            {
                Vector2 center = (from + to) * 0.5f;
                float radiusX = (to.x - from.x) * 0.5f;
                float radiusY = (to.y - from.y) * 0.5f;
                for (int i = 0; i <= 32; i++)
                {
                    float angle = i * Mathf.PI * 2f / 32f;
                    AddPoint(new Vector2(center.x + radiusX * Mathf.Cos(angle), center.y + radiusY * Mathf.Sin(angle)));
                }
                return;
            }

            if (_tool == ToolArrow)
            {
                Vector2 direction = to - from;
                float length = direction.magnitude;
                if (length < 0.0001f) return;

                Vector2 unit = direction / length;
                Vector2 side = new Vector2(-unit.y, unit.x);
                float head = Mathf.Min(0.25f, length * 0.35f);

                AddPoint(from);
                AddPoint(to);
                AddPoint(to - unit * head + side * head * 0.5f);
                AddPoint(to);
                AddPoint(to - unit * head - side * head * 0.5f);
            }
        }

        private void EraseAt(Vector2 point)
        {
            float radius = widths[_widthIndex] * 3f;
            bool removed = false;

            for (int stroke = _strokes - 1; stroke >= 0; stroke--)
            {
                if (!StrokeNear(stroke, point, radius)) continue;

                TakeOwnership();
                RemoveStroke(stroke);
                removed = true;
            }

            if (!removed) return;

            _activeStroke = -1;
            Rebuild();
            if (Time.timeSinceLevelLoad < _nextSync) return;

            _nextSync = Time.timeSinceLevelLoad + SyncInterval;
            PushSync();
        }

        private bool StrokeNear(int stroke, Vector2 point, float radius)
        {
            int start = 0;
            for (int i = 0; i < stroke; i++) start += _count[i];

            for (int i = 0; i < _count[stroke]; i++)
            {
                float dx = _pointX[start + i] - point.x;
                float dy = _pointY[start + i] - point.y;
                if (dx * dx + dy * dy <= radius * radius) return true;
            }
            return false;
        }

        private void RemoveStroke(int stroke)
        {
            int start = 0;
            for (int i = 0; i < stroke; i++) start += _count[i];
            int length = _count[stroke];

            for (int i = start; i < _points - length; i++)
            {
                _pointX[i] = _pointX[i + length];
                _pointY[i] = _pointY[i + length];
            }
            for (int i = stroke; i < _strokes - 1; i++)
            {
                _color[i] = _color[i + 1];
                _width[i] = _width[i + 1];
                _count[i] = _count[i + 1];
            }
            _points -= length;
            _strokes--;
        }

        private void Rebuild()
        {
            int pointIndex = 0;
            for (int i = 0; i < strokeLines.Length; i++)
            {
                LineRenderer line = strokeLines[i];
                if (line == null) continue;

                if (i >= _strokes)
                {
                    line.gameObject.SetActive(false);
                    continue;
                }

                int count = _count[i];
                line.gameObject.SetActive(count > 1);
                line.positionCount = count;
                line.widthMultiplier = widths[_width[i]];
                Color color = palette[_color[i]];
                line.startColor = color;
                line.endColor = color;

                for (int p = 0; p < count; p++)
                {
                    line.SetPosition(p, new Vector3(_pointX[pointIndex + p], _pointY[pointIndex + p], 0f));
                }
                pointIndex += count;
            }

            if (drawCamera == null) return;

            drawCamera.enabled = true;
            _renderFrames = 2;
        }

        private void PushSync()
        {
            if (!Networking.IsOwner(gameObject)) return;

            _syncColor = new int[_strokes];
            _syncWidth = new int[_strokes];
            _syncCount = new int[_strokes];
            for (int i = 0; i < _strokes; i++)
            {
                _syncColor[i] = _color[i];
                _syncWidth[i] = _width[i];
                _syncCount[i] = _count[i];
            }

            _syncX = new float[_points];
            _syncY = new float[_points];
            for (int i = 0; i < _points; i++)
            {
                _syncX[i] = _pointX[i];
                _syncY[i] = _pointY[i];
            }
            RequestSerialization();
        }

        private void TakeOwnership()
        {
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        private void UpdateToolText()
        {
            string tool = "Пензель";
            if (_tool == ToolLine) tool = "Лінія";
            else if (_tool == ToolRect) tool = "Прямокутник";
            else if (_tool == ToolEllipse) tool = "Еліпс";
            else if (_tool == ToolArrow) tool = "Стрілка";
            else if (_tool == ToolEraser) tool = "Гумка";

            string width = "тонко";
            if (_widthIndex == 1) width = "середньо";
            else if (_widthIndex == 2) width = "товсто";

            string line = tool + ", " + width + ", колір " + (_colorIndex + 1);
            foreach (Text text in toolTexts)
            {
                if (text != null) text.text = line;
            }
        }
    }
}
