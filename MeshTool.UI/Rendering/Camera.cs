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

        public Matrix4X4<float> GetViewMatrix()
        {
            var forward = GetForward();
            return Matrix4X4.CreateLookAt(Position, Position + forward, new Vector3D<float>(0, 1, 0));
        }

        public Matrix4X4<float> GetProjectionMatrix(float width, float height)
        {
            float aspectRatio = width / MathF.Max(1.0f, height);
            float zFar = 100000.0f;
            return Matrix4X4.CreatePerspectiveFieldOfView(MathF.PI / 3.0f, aspectRatio, 0.1f, zFar);
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
            var fwd = GetForward();
            var rightVec = Vector3D.Normalize(Vector3D.Cross(fwd, new Vector3D<float>(0, 1, 0)));
            var upVec = new Vector3D<float>(0, 1, 0);

            float speed = MoveSpeed * (sprint ? 3.0f : 1.0f);
            var velocity = (fwd * forward + rightVec * right + upVec * up) * speed * deltaSeconds;
            Position += velocity;
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
            Position = center - forward * (radius * 2.5f);
            MoveSpeed = MathF.Max(20.0f, radius * 0.5f);
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
