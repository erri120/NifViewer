using Silk.NET.Maths;
using Triangle = Silk.NET.Maths.Vector3D<ushort>;
using Edge = Silk.NET.Maths.Vector2D<float>;

namespace NifViewer.Lib
{
    public static class NiflyUtils
    {
        public static Vector2D<float> ToVector2D(this nifly.Vector2 vector2)
        {
            return new Vector2D<float>(vector2.u, vector2.v);
        }

        public static Vector3D<float> ToVector3D(this nifly.Vector3 vector3)
        {
            return new Vector3D<float>(vector3.x, vector3.y, vector3.z);
        }

        public static Vector3D<float> ToVector3D(this nifly.Color4 color4)
        {
            return new Vector3D<float>(color4.r, color4.g, color4.b);
        }

        public static Triangle ToTriangle(this nifly.Triangle triangle)
        {
            return new Triangle(triangle.p1, triangle.p2, triangle.p3);
        }
    }
}
