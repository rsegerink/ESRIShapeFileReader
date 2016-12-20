/* ------------------------------------------------------------------------
 * (c)copyright 2009-2012 Catfood Software and contributors - http://catfood.net
 * Provided under the ms-PL license, see LICENSE.txt
 * 
 * ------------------------------------------------------------------------ */

/* ------------------------------------------------------------------------
 * 
 * (c)copyright 2016 SnapIT
 * Changed reading of Dbf metadata by using NDbfReader instead of OleDB reader,
 * this solves any encoding issues.
 * 
 * ------------------------------------------------------------------------ */

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.IO;

using NDbfReader;

namespace ESRIShapeFileReader
{
    /// <summary>
    /// Provides a readonly IEnumerable interface to an ERSI Shapefile.
    /// NOTE - has not been designed to be thread safe
    /// </summary>
    /// <remarks>
    /// See the ESRI Shapefile specification at http://www.esri.com/library/whitepapers/pdfs/shapefile.pdf
    /// </remarks>
    public class Shapefile : IDisposable, IEnumerator<Shape>, IEnumerable<Shape>
    {
        private const string MainPathExtension = "shp";
        private const string IndexPathExtension = "shx";
        private const string DbasePathExtension = "dbf";

        private bool _disposed;
        private bool _opened;
        private bool _rawMetadataOnly;
        private int _currentIndex = -1;
        private int _count;
        private RectangleD _boundingBox;
        private ShapeType _type;
        private string _shapefileMainPath;
        private string _shapefileIndexPath;
        private string _shapefileDbasePath;
        private FileStream _mainStream;
        private FileStream _indexStream;
        private Header _mainHeader;
        private Header _indexHeader;

        private Encoding _encoding;
        private Table _table;
        private Reader _reader;

        /// <summary>
        /// Create a new Shapefile object.
        /// </summary>
        public Shapefile()
            : this(null, Encoding.UTF8) {}

        /// <summary>
        /// Create a new Shapefile object and open a Shapefile. Note that three files are required - 
        /// the main file (.shp), the index file (.shx) and the dBASE table (.dbf). The three files 
        /// must all have the same filename (i.e. shapes.shp, shapes.shx and shapes.dbf). Set path
        /// to any one of these three files to open the Shapefile.
        /// </summary>
        /// <param name="path">Path to the .shp, .shx or .dbf file for this Shapefile</param>
        /// <exception cref="ObjectDisposedException">Thrown if the Shapefile has been disposed</exception>
        /// <exception cref="ArgumentException">Thrown if the path parameter is empty</exception>
        /// <exception cref="FileNotFoundException">Thrown if one of the three required files is not found</exception>
        public Shapefile(string path)
            : this(path, Encoding.UTF8) {}

        /// <summary>
        /// Create a new Shapefile object and open a Shapefile. Note that three files are required - 
        /// the main file (.shp), the index file (.shx) and the dBASE table (.dbf). The three files 
        /// must all have the same filename (i.e. shapes.shp, shapes.shx and shapes.dbf). Set path
        /// to any one of these three files to open the Shapefile.
        /// </summary>
        /// <param name="path">Path to the .shp, .shx or .dbf file for this Shapefile</param>
        /// <param name="encoding">Encoding used for reading the dbf metadata, default is UTF-8</param>
        /// <exception cref="ObjectDisposedException">Thrown if the Shapefile has been disposed</exception>
        /// <exception cref="ArgumentNullException">Thrown if the connectionStringTemplate parameter is null</exception>
        /// <exception cref="ArgumentException">Thrown if the path parameter is empty</exception>
        /// <exception cref="FileNotFoundException">Thrown if one of the three required files is not found</exception>
        public Shapefile(string path, Encoding encoding)
        {
            _encoding = encoding;

            if (path != null)
            {
                Open(path);
            }
        }

        /// <summary>
        /// Create a new Shapefile object and open a Shapefile. Note that three files are required - 
        /// the main file (.shp), the index file (.shx) and the dBASE table (.dbf). The three files 
        /// must all have the same filename (i.e. shapes.shp, shapes.shx and shapes.dbf). Set path
        /// to any one of these three files to open the Shapefile.
        /// </summary>
        /// <param name="path">Path to the .shp, .shx or .dbf file for this Shapefile</param>
        /// <exception cref="ObjectDisposedException">Thrown if the Shapefile has been disposed</exception>
        /// <exception cref="ArgumentNullException">Thrown if the path parameter is null</exception>
        /// <exception cref="ArgumentException">Thrown if the path parameter is empty</exception>
        /// <exception cref="FileNotFoundException">Thrown if one of the three required files is not found</exception>
        /// <exception cref="InvalidOperationException">Thrown if an error occurs parsing file headers</exception>
        public void Open(string path)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("Shapefile");
            }

            if (path == null)
            {
                throw new ArgumentNullException("path");
            }
            if (path.Length <= 0)
            {
                throw new ArgumentException("path parameter is empty", "path");
            }

            _shapefileMainPath = Path.ChangeExtension(path, MainPathExtension);
            _shapefileIndexPath = Path.ChangeExtension(path, IndexPathExtension);
            _shapefileDbasePath = Path.ChangeExtension(path, DbasePathExtension);

            if (!File.Exists(_shapefileMainPath))
            {
                throw new FileNotFoundException("Shapefile main file not found", _shapefileMainPath);
            }
            if (!File.Exists(_shapefileIndexPath))
            {
                throw new FileNotFoundException("Shapefile index file not found", _shapefileIndexPath);
            }
            if (!File.Exists(_shapefileDbasePath))
            {
                throw new FileNotFoundException("Shapefile dBase file not found", _shapefileDbasePath);
            }

            _mainStream = File.Open(_shapefileMainPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _indexStream = File.Open(_shapefileIndexPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            if (_mainStream.Length < Header.HeaderLength)
            {
                throw new InvalidOperationException("Shapefile main file does not contain a valid header");
            }

            if (_indexStream.Length < Header.HeaderLength)
            {
                throw new InvalidOperationException("Shapefile index file does not contain a valid header");
            }

            // read in and parse the headers
            byte[] headerBytes = new byte[Header.HeaderLength];
            _mainStream.Read(headerBytes, 0, Header.HeaderLength);
            _mainHeader = new Header(headerBytes);
            _indexStream.Read(headerBytes, 0, Header.HeaderLength);
            _indexHeader = new Header(headerBytes);

            // set properties from the main header
            _type = _mainHeader.ShapeType;
            _boundingBox = new RectangleD(_mainHeader.XMin, _mainHeader.YMin, _mainHeader.XMax, _mainHeader.YMax);

            // index header length is in 16-bit words, including the header - number of 
            // shapes is the number of records (each 4 workds long) after subtracting the header bytes
            _count = (_indexHeader.FileLength - (Header.HeaderLength / 2)) / 4;

            // open the metadata database
            OpenDb(_encoding);

            _opened = true;
        }

        /// <summary>
        /// Close the Shapefile. Equivalent to calling Dispose().
        /// </summary>
        public void Close()
        {
            Dispose();
        }

        /// <summary>
        /// If true then only the IDataRecord (DataRecord) property is available to access metadata for each shape.
        /// If flase (the default) then metadata is also parsed into a string dictionary (use GetMetadataNames() and
        /// GetMetadata() to access)
        /// </summary>
        public bool RawMetadataOnly
        {
            get { return _rawMetadataOnly; }
            set { _rawMetadataOnly = value; }
        }

        /// <summary>
        /// Gets the number of shapes in the Shapefile
        /// </summary>
        public int Count
        {
            get 
            {
                if (_disposed) throw new ObjectDisposedException("Shapefile");
                if (!_opened) throw new InvalidOperationException("Shapefile not open.");

                return _count; 
            }
        }

        /// <summary>
        /// Gets the bounding box for the Shapefile
        /// </summary>
        public RectangleD BoundingBox
        {
            get 
            {
                if (_disposed) throw new ObjectDisposedException("Shapefile");
                if (!_opened) throw new InvalidOperationException("Shapefile not open.");

                return _boundingBox; 
            }
           
        }

        /// <summary>
        /// Gets the ShapeType of the Shapefile
        /// </summary>
        public ShapeType Type
        {
            get 
            {
                if (_disposed) throw new ObjectDisposedException("Shapefile");
                if (!_opened) throw new InvalidOperationException("Shapefile not open.");
                
                return _type; 
            }
        }

        private void OpenDb(Encoding encoding)
        {
            _table = Table.Open(_shapefileDbasePath);
            _reader = _table.OpenReader(encoding); 
        }

        private void CloseDb()
        {
            if (_reader != null)
            {
                _reader = null;
            }

            if (_table != null)
            {
                _table.Dispose();
                _table = null;
            }
        }

        #region IDisposable Members

        /// <summary />
        ~Shapefile()
        {
            Dispose(false);
        }

        /// <summary>
        /// Dispose the Shapefile and free all resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool canDisposeManagedResources)
        {
            if (!_disposed)
            {
                if (canDisposeManagedResources)
                {
                    if (_mainStream != null)
                    {
                        _mainStream.Close();
                        _mainStream = null;
                    }

                    if (_indexStream != null)
                    {
                        _indexStream.Close();
                        _indexStream = null;
                    }

                    CloseDb();
                }

                _disposed = true;
                _opened = false;
            }
        }

        #endregion

        #region IEnumerator<Shape> Members

        /// <summary>
        /// Gets the current shape in the collection
        /// </summary>
        public Shape Current
        {
            get 
            {
                if (_disposed) throw new ObjectDisposedException("Shapefile");
                if (!_opened) throw new InvalidOperationException("Shapefile not open.");
               
                // get the metadata
                StringDictionary metadata = null;
                if (!RawMetadataOnly)
                {
                    metadata = new StringDictionary();

                    for (int i = 0; i < _reader.Table.Columns.Count; i++)
                    {
                        var column = _reader.Table.Columns[i];
                        var value = _reader.GetValue(column);

                        metadata.Add(column.Name, 
                            value != null ? value.ToString() : null);
                    }
                }

                // get the index record
                byte[] indexHeaderBytes = new byte[8];
                _indexStream.Seek(Header.HeaderLength + _currentIndex * 8, SeekOrigin.Begin);
                _indexStream.Read(indexHeaderBytes, 0, indexHeaderBytes.Length);
                int contentOffsetInWords = EndianBitConverter.ToInt32(indexHeaderBytes, 0, ProvidedOrder.Big);
                int contentLengthInWords = EndianBitConverter.ToInt32(indexHeaderBytes, 4, ProvidedOrder.Big);

                // get the data chunk from the main file - need to factor in 8 byte record header
                int bytesToRead = (contentLengthInWords * 2) + 8;
                byte[] shapeData = new byte[bytesToRead];
                _mainStream.Seek(contentOffsetInWords * 2, SeekOrigin.Begin);
                _mainStream.Read(shapeData, 0, bytesToRead);

                return ShapeFactory.ParseShape(shapeData, metadata, _reader);
            }
        }

        #endregion

        #region IEnumerator Members

        /// <summary>
        /// Gets the current item in the collection
        /// </summary>
        object System.Collections.IEnumerator.Current
        {
            get 
            {
                if (_disposed) throw new ObjectDisposedException("Shapefile");
                if (!_opened) throw new InvalidOperationException("Shapefile not open.");

                return this.Current; 
            }
        }

        /// <summary>
        /// Move to the next item in the collection (returns false if at the end)
        /// </summary>
        /// <returns>false if there are no more items in the collection</returns>
        public bool MoveNext()
        {
            if (_disposed) throw new ObjectDisposedException("Shapefile");
            if (!_opened) throw new InvalidOperationException("Shapefile not open.");

            if (_currentIndex++ < (_count - 1))
            {
                // try to read the next database record
                if (!_reader.Read())
                {
                    throw new InvalidOperationException("Metadata database does not contain a record for the next shape");
                }

                // try to read the next database record
                /*if (!_dbReader.Read())
                {
                    throw new InvalidOperationException("Metadata database does not contain a record for the next shape");
                }*/

                return true;
            }
            else
            {
                // reached the last shape
                return false;
            }
        }

        /// <summary>
        /// Reset the enumerator
        /// </summary>
        public void Reset()
        {
            if (_disposed) throw new ObjectDisposedException("Shapefile");
            if (!_opened) throw new InvalidOperationException("Shapefile not open.");

            CloseDb();
            OpenDb(_encoding);
            _currentIndex = -1;
        }

        #endregion

        #region IEnumerable<Shape> Members

        /// <summary>
        /// Get the IEnumerator for this Shapefile
        /// </summary>
        /// <returns>IEnumerator</returns>
        public IEnumerator<Shape> GetEnumerator()
        {
            return (IEnumerator<Shape>)this;
        }

        #endregion

        #region IEnumerable Members

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return (System.Collections.IEnumerator)this;
        }

        #endregion
    }
}
