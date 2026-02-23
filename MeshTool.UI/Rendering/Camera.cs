using Silk.NET.Maths;
using System;

namespace MeshTool.UI.Rendering
{
    public class Camera
    {
        public Vector3D<float> Position { get; private set; } = new Vector3D<float>(0, 50, 150);
        public float Pitch { get; private set; } = -0.25f;
        public float Yaw { get; private set; } = MathF.PI * 1.25f;
        public float MoveSpeed { get; set; } = 120.0f;

        private Vector3D<float> _targetPosition = new Vector3D<float>(0, 50, 150);
        private float _targetMoveSpeed = 120.0f;
        private bool _isLerping = false;

        public Matrix4X4<float> GetViewMatrix()
        {
            var forward = GetForward();
            return Matrix4X4.CreateLookAt(Position, Position + forward, new Vector3D<float>(0, 1, 0));
        }

        public Matrix4X4<float> GetProjectionMatrix(float width, float height)
        {
            float aspectRatio = width / MathF.Max(1.0f, height);
            float zNear = 0.1f;
            float fov = MathF.PI / 3.0f;

            float tanHalfFov = MathF.Tan(fov / 2.0f);

            // OpenGL-style reverse-Z projection with infinite far plane.
            // Maps zNear to z_ndc = 1, and z -> +infinity to z_ndc = -1.
            var proj = new Matrix4X4<float>();
            proj.M11 = 1.0f / (aspectRatio * tanHalfFov);
            proj.M22 = 1.0f / tanHalfFov;
            proj.M33 = 1.0f;
            proj.M34 = -1.0f;
            proj.M43 = 2.0f * zNear;
            proj.M44 = 0.0f;

            return proj;
        }

        public void Look(float dx, float dy)
        {
            const float sensitivity = 0.0035f;
            Yaw += dx * sensitivity;
            Pitch -= dy * sensitivity;

            float maxPitch = MathF.PI / 2.0f - 0.01f;
            if (Pitch > maxPitch) Pitch = maxPitch;
            if (Pitch < -maxPitch) Pitch = -maxPitch;
        }

        public void Move(float forward, float right, float up, float deltaSeconds, bool sprint)
        {
            _isLerping = false; // Cancel lerp on manual move
            var fwd = GetForward();
            var rightVec = Vector3D.Normalize(Vector3D.Cross(fwd, new Vector3D<float>(0, 1, 0)));
            var upVec = new Vector3D<float>(0, 1, 0);

            float speed = MoveSpeed * (sprint ? 3.0f : 1.0f);
            var velocity = (fwd * forward + rightVec * right + upVec * up) * speed * deltaSeconds;
            Position += velocity;
            _targetPosition = Position;
        }

        public void Zoom(float delta)
        {
            MoveSpeed *= MathF.Pow(1.15f, delta);
            if (MoveSpeed < 1.0f) MoveSpeed = 1.0f;
            if (MoveSpeed > 20000.0f) MoveSpeed = 20000.0f;
        }

        public void FocusOnBounds(Vector3D<float> center, Vector3D<float> extents)
        {
            float radius = MathF.Max(10.0f, MathF.Max(extents.X, MathF.Max(extents.Y, extents.Z)) * 0.8f);
            var forward = GetForward();
            _targetPosition = center - forward * (radius * 2.5f);
            _targetMoveSpeed = MathF.Max(20.0f, radius * 0.5f);
            
            // If we are extremely far, just snap to avoid a 10 minute lerp
            if (Vector3D.Distance(Position, _targetPosition) > radius * 50.0f)
            {
                Position = _targetPosition;
                MoveSpeed = _targetMoveSpeed;
            }
            else
            {
                _isLerping = true;
            }
        }

        public bool UpdateLerp(float dt)
        {
            if (!_isLerping) return false;

            float lerpFactor = 1.0f - MathF.Exp(-10.0f * dt); // smooth exponential decay
            Position = Vector3D.Lerp(Position, _targetPosition, lerpFactor);
            MoveSpeed = MoveSpeed + (_targetMoveSpeed - MoveSpeed) * lerpFactor;

            if (Vector3D.DistanceSquared(Position, _targetPosition) < 0.01f)
            {
                Position = _targetPosition;
                MoveSpeed = _targetMoveSpeed;
                _isLerping = false;
            }
            return true;
        }

        public (Vector3D<float> Origin, Vector3D<float> Direction) GetRay(float mouseX, float mouseY, float screenWidth, float screenHeight)
        {
            float w = MathF.Max(1.0f, screenWidth);
            float h = MathF.Max(1.0f, screenHeight);
            float ndcX = (mouseX / w) * 2.0f - 1.0f;
            float ndcY = 1.0f - (mouseY / h) * 2.0f; // Invert Y

            float aspectRatio = w / h;
            float fov = MathF.PI / 3.0f;
            float tanHalfFov = MathF.Tan(fov / 2.0f);

            var forward = GetForward();
            var upWorld = new Vector3D<float>(0, 1, 0);
            var right = Vector3D.Normalize(Vector3D.Cross(forward, upWorld));
            var up = Vector3D.Normalize(Vector3D.Cross(right, forward));

            var dir = Vector3D.Normalize(
                forward +
                right * (ndcX * aspectRatio * tanHalfFov) +
                up * (ndcY * tanHalfFov));

            return (Position, dir);
        }

        private Vector3D<float> GetForward()
        {
            float x = MathF.Cos(Pitch) * MathF.Cos(Yaw);
            float y = MathF.Sin(Pitch);
            float z = MathF.Cos(Pitch) * MathF.Sin(Yaw);
            return Vector3D.Normalize(new Vector3D<float>(x, y, z));
        }
    }
}
