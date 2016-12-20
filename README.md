ESRIShapeFileReader
============

Reader for reading ESRI shapefiles, including any metadata.  All 2D shapes are supported: Point, MultiPoint, PolyLine and Polygon. 

Based on Catfood.Shapefile 1.51 (http://shapefile.codeplex.com/) but it uses NDbfReader (https://github.com/eXavera/NDbfReader)
for reading the dBase metadata this solves any encoding issues, see: http://shapefile.codeplex.com/discussions/287190

## Example

To enumerate shapes pass the path to any of these three files to the Shapefile constructor and then use the IEnumerable<Shape> interface as demonstrated below:
```csharp
using (Shapefile shapefile = new Shapefile("my.shp")
{
    foreach (Shape shape in shapefile)
    {
        Console.WriteLine("ShapeType: {0}", shape.Type);
    }
}
```
Shape is the base class for a set of more specific classes - ShapePoint, ShapeMultiPoint, ShapePolyLine and ShapePolygon. Cast to the appropriate class based on the Type property:
```
switch (shape.Type)
{
    case ShapeType.Point:
        ShapePoint shapePoint = shape as ShapePoint;
        Console.WriteLine("Point={0},{1}", shapePoint.Point.X, shapePoint.Point.Y);
        break;

    // ...
}
```
Access metadata for the shape using GetMetadataNames() to list available names (keys) and GetMetadata() to access metadata by name.

See the ShapefileDemo project for a sample command line application that dumps information for each shape in a shapefile.