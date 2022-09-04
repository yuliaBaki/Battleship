using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;


/// <summary>
/// Generic bidimensional array with 0-based indexes, visually starting at the top-left corner.
/// </summary>
public class Matrix<T> : IEnumerable<MatrixCell<T>>, IEnumerable
{
	public const OutOfBoundsRule DefaultOutOfBoundsRule = OutOfBoundsRule.Exception;
	protected T[,] Grid { get; set; } = new T[0, 0];

	#region Constructors

	public Matrix() : this(0, 0) { }
	public Matrix(Size size, Func<Position, T> defaultValues = null) : this(size.Width, size.Height, defaultValues) { }
	public Matrix(int width, int height, Func<Position, T> defaultValues = null) : this(new T[width, height])
	{
		if (defaultValues != null)
		{
			foreach (var cell in this.ToList())
			{
				this[cell.Position] = defaultValues(cell.Position);
			}
		}
	}
	public Matrix(Size size, T defaultValue) : this(size.Width, size.Height, defaultValue) { }
	public Matrix(int width, int height, T defaultValue) : this(width, height, (p) => defaultValue) { }
	public Matrix(Matrix<T> matrix) : this(matrix.Grid) { }
	public Matrix(T[,] grid)
	{
		Grid = grid;
	}

	#endregion

	#region Misc Getters

	public T this[int x, int y]
	{
		get => Grid[x, y];
		set => Grid[x, y] = value;
	}
	public T this[Position position]
	{
		get => this[position.X, position.Y];
		set => this[position.X, position.Y] = value;
	}

	public int Width => Grid.GetLength(0);
	public int Height => Grid.GetLength(1);
	public Size Size => new Size(this.Width, this.Height);
	public virtual Position Position => new Position(0, 0);

	#endregion

	#region Complex getters

	public MatrixCell<T>[] GetCells(Func<MatrixCell<T>, bool> predicate = null)
	{
		return predicate == null ? this.ToArray() : this.Where(predicate).ToArray();
	}
	public MatrixCell<T>[] GetEdgeCells()
	{
		return GetCells(z => z.IsOnEdge);
	}
	public MatrixCell<T>[] GetCornerCells()
	{
		return GetCells(z => z.IsOnCorner);
	}

	public MatrixCell<T>? GetCell(int x, int y, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule) { return GetCell(new Position(x, y), outOfBoundsRule); }
	public MatrixCell<T>? GetCell(Position position, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule)
	{
		return outOfBoundsRule switch
		{
			// If out of bounds, return no cell
			OutOfBoundsRule.Ignore => IsOutOfBounds(position) ? default(MatrixCell<T>?) : new MatrixCell<T>(this, position, this[position]),

			// If out of bounds, return a valid empty cell
			OutOfBoundsRule.IncludeNull => new MatrixCell<T>(this, position, IsOutOfBounds(position) ? default(T) : this[position]),

			// Else, proceed normally
			_ => new MatrixCell<T>(this, position, this[position]),
		};
	}
	public Matrix<T> GetCellAsSubmatrix(int x, int y, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule) { return GetCellAsSubmatrix(new Position(x, y), outOfBoundsRule); }
	public Matrix<T> GetCellAsSubmatrix(Position position, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule)
	{

		return GetSubmatrix(new Bounds(position, 1, 1), outOfBoundsRule);
	}
	public MatrixCell<T>[] GetColumn(Position position, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule) { return GetColumn(position.X, outOfBoundsRule); }
	public MatrixCell<T>[] GetColumn(int x, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule)
	{
		var result = new MatrixCell<T>[this.Height];
		if (outOfBoundsRule == OutOfBoundsRule.Ignore && IsOutOfBounds(x, 0))
			return new MatrixCell<T>[0];

		for (var y = 0; y < this.Height; y++)
		{
			result[y] = GetCell(x, y, outOfBoundsRule).Value;
		}
		return result;
	}
	public Matrix<T> GetColumnAsSubmatrix(Position position, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule) { return GetColumnAsSubmatrix(position.X, outOfBoundsRule); }
	public Matrix<T> GetColumnAsSubmatrix(int x, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule)
	{
		return GetSubmatrix(new Bounds(x, 0, 1, this.Height), outOfBoundsRule);
	}
	public MatrixCell<T>[] GetRow(Position position, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule) { return GetRow(position.Y, outOfBoundsRule); }
	public MatrixCell<T>[] GetRow(int y, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule)
	{
		var result = new MatrixCell<T>[this.Width];
		if (outOfBoundsRule == OutOfBoundsRule.Ignore && IsOutOfBounds(0, y))
			return new MatrixCell<T>[0];

		for (var x = 0; x < this.Width; x++)
		{
			result[x] = GetCell(x, y, outOfBoundsRule).Value;
		}
		return result;
	}
	public Matrix<T> GetRowAsSubmatrix(Position position, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule) { return GetRowAsSubmatrix(position.Y, outOfBoundsRule); }
	public Matrix<T> GetRowAsSubmatrix(int y, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule)
	{
		return GetSubmatrix(new Bounds(0, y, this.Width, 1), outOfBoundsRule);
	}

	#endregion

	#region Complex setters and Size manipulation

	/// <summary>
	/// Default values = Adds 1 empty column to the right
	/// </summary>
	public void AddColumns(int? index = null, int count = 1)
	{
		index ??= this.Width;
		if (count < 0)
			throw new InvalidOperationException("Cannot add a negative amount of columns.");

		var result = new Matrix<T>(this.Width + count, this.Height);

		for (var x = 0; x < this.Width; x++)
		{
			for (var y = 0; y < this.Height; y++)
			{
				result[x >= index ? (x + count) : x, y] = this[x, y];
			}
		}
		this.Grid = result.Grid;
	}

	/// <summary>
	/// Default values = Adds 1 empty row at the bottom
	/// </summary>
	public void AddRows(int? index = null, int count = 1)
	{
		index ??= this.Height;
		if (count < 0)
			throw new InvalidOperationException("Cannot add a negative amount of rows.");

		var result = new Matrix<T>(this.Width, this.Height + count);

		for (var x = 0; x < this.Width; x++)
		{
			for (var y = 0; y < this.Height; y++)
			{
				result[y, y >= index ? (y + count) : y] = this[x, y];
			}
		}
		this.Grid = result.Grid;
	}

	public void SetColumn(int x, T[] column, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule)
	{
		SetSubmatrix(new Position(x, 0), Matrix.FromColumn(column), outOfBoundsRule);
	}
	public void SetRow(int y, T[] row, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule)
	{
		SetSubmatrix(new Position(0, y), Matrix.FromRow(row), outOfBoundsRule);
	}

	public void AppendColumn(T[] column, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule)
	{
		AddColumns();
		SetColumn(this.Width - 1, column, outOfBoundsRule);
	}
	public void AppendRow(T[] row, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule)
	{
		AddRows();
		SetRow(this.Height - 1, row, outOfBoundsRule);
	}

	public void InsertColumn(int index, T[] column, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule)
	{
		AddColumns(index, 1);
		SetColumn(index, column, outOfBoundsRule);
	}
	public void InsertRow(int index, T[] row, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule)
	{
		AddRows(index, 1);
		SetRow(index, row, outOfBoundsRule);
	}

	public void RemoveColumns(int index, int count = 1)
	{
		if (count < 0)
			throw new InvalidOperationException("Cannot remove a negative amount of columns.");

		var result = new Matrix<T>(this.Width - count, this.Height);

		for (var x = 0; x < result.Width; x++)
		{
			for (var y = 0; y < result.Height; y++)
			{
				result[x, y] = this[x >= index ? (x + count) : x, y];
			}
		}
		this.Grid = result.Grid;
	}
	public void RemoveRows(int index, int count = 1)
	{
		if (count < 0)
			throw new InvalidOperationException("Cannot remove a negative amount of rows.");

		var result = new Matrix<T>(this.Width, this.Height - count);

		for (var x = 0; x < result.Width; x++)
		{
			for (var y = 0; y < result.Height; y++)
			{
				result[x, y] = this[x, y >= index ? (y + count) : y];
			}
		}
		this.Grid = result.Grid;
	}

	public void ClearColumns(int index, int count = 1)
	{
		if (count < 0)
			throw new InvalidOperationException("Cannot clear a negative amount of columns.");

		SetSubmatrix(new Position(index, 0), new Matrix<T>(count, this.Height), OutOfBoundsRule.Ignore);
	}
	public void ClearRows(int index, int count = 1)
	{
		if (count < 0)
			throw new InvalidOperationException("Cannot clear a negative amount of rows.");

		SetSubmatrix(new Position(0, index), new Matrix<T>(this.Width, count), OutOfBoundsRule.Ignore);
	}

	#endregion

	#region Submatrix & Bounds tools

	/// <summary>
	/// Returns a smaller section of the matrix, defined by a position and a size.
	/// </summary>
	public Submatrix<T> GetSubmatrix(Bounds bounds, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule)
	{
		bounds = bounds.Positify();

		if (outOfBoundsRule == OutOfBoundsRule.Ignore)
		{
			bounds = Matrix.GetBoundsIntersection(bounds, new Bounds(0, 0, this.Size));
		}

		var result = new Submatrix<T>(bounds);

		if (result.Size.Total == 0)
		{
			return new Submatrix<T>();
		}

		foreach (var resultCell in result.ToList())
		{
			if (outOfBoundsRule == OutOfBoundsRule.IncludeNull && IsOutOfBounds(resultCell.MasterPosition))
				result[resultCell.Position] = default(T);
			else
				result[resultCell.Position] = this[resultCell.MasterPosition];
		}

		return result;
	}

	/// <summary>
	/// Copies the values of the subMatrix into this Matrix.
	/// In this particular method, OutOfBoundsRule.Ignore ignores values outside of this Matrix's bounds, and OutOfBoundsRule.IncludeNull expands this Matrix to fit the subMatrix.
	/// </summary>
	public void SetSubmatrix(Position position, Matrix<T> subMatrix, OutOfBoundsRule outOfBoundsRule = DefaultOutOfBoundsRule)
	{
		var thisMatrixPosition = new Position(0, 0);
		var subMatrixPosition = position;

		if (outOfBoundsRule == OutOfBoundsRule.IncludeNull)
		{
			var unionBounds = Matrix.GetBoundsUnion(new Bounds(thisMatrixPosition, this.Size), new Bounds(subMatrixPosition, subMatrix.Size));

			if (unionBounds.Size.Total != this.Size.Total) // If we have to enlarge
			{
				// Offset both matrixes relative to the new bounds
				thisMatrixPosition = new Position(thisMatrixPosition.X - unionBounds.Position.X, thisMatrixPosition.Y - unionBounds.Position.Y);
				subMatrixPosition = new Position(subMatrixPosition.X - unionBounds.Position.X, subMatrixPosition.Y - unionBounds.Position.Y);

				// Copy contents of current Matrix into the larger temporary one

				var newMatrix = new Matrix<T>(unionBounds.Size);

				for (var thisX = 0; thisX < this.Width; thisX++)
				{
					for (var thisY = 0; thisY < this.Height; thisY++)
					{
						newMatrix[thisX + thisMatrixPosition.X, thisY + thisMatrixPosition.Y] = this[thisX, thisY];
					}
				}

				// Current Matrix becomes larger one

				this.Grid = newMatrix.Grid;
			}
		}
		for (var subMatrixX = 0; subMatrixX < subMatrix.Width; subMatrixX++)
		{
			var thisX = subMatrixX + subMatrixPosition.X;

			for (var subMatrixY = 0; subMatrixY < subMatrix.Height; subMatrixY++)
			{
				var thisY = subMatrixY + subMatrixPosition.Y;

				if (outOfBoundsRule == OutOfBoundsRule.Ignore && IsOutOfBounds(thisX, thisY))
					continue;

				this[thisX, thisY] = subMatrix[subMatrixX, subMatrixY];
			}
		}

	}

	/// <summary>
	/// Shortcut for MatchCells where the results will only be located inside this Matrix AND the subMatrix.
	/// </summary>
	public IEnumerable<(MatrixCell<T> MatrixCell, MatrixCell<T2> SubMatrixCell)> MatchCellsIntersection<T2>(Position subMatrixPosition, Matrix<T2> subMatrix)
	{
		var workingBounds = Matrix.GetBoundsIntersection(new Bounds(0, 0, this.Size), new Bounds(subMatrixPosition, subMatrix.Size));
		return MatchCells(workingBounds, subMatrixPosition, subMatrix).Select(z => (z.MatrixCell.Value, z.SubMatrixCell.Value));
	}

	/// <summary>
	/// Shortcut for MatchCells where the results can be located inside this Matrix and/or inside the subMatrix.
	/// </summary>
	public IEnumerable<(MatrixCell<T>? MatrixCell, MatrixCell<T2>? SubMatrixCell)> MatchCellsUnion<T2>(Position subMatrixPosition, Matrix<T2> subMatrix)
	{
		var workingBounds = Matrix.GetBoundsUnion(new Bounds(0, 0, this.Size), new Bounds(subMatrixPosition, subMatrix.Size));
		return MatchCells(workingBounds, subMatrixPosition, subMatrix);
	}

	/// <summary>
	/// Shortcut for MatchCells where the results will only be located inside this Matrix.
	/// </summary>
	public IEnumerable<(MatrixCell<T> MatrixCell, MatrixCell<T2>? SubMatrixCell)> MatchCellsOnlyThis<T2>(Position subMatrixPosition, Matrix<T2> subMatrix)
	{
		var workingBounds = new Bounds(0, 0, this.Size);
		return MatchCells(workingBounds, subMatrixPosition, subMatrix).Select(z => (z.MatrixCell.Value, z.SubMatrixCell));
	}

	/// <summary>
	/// Shortcut for MatchCells where the results will only be located inside the subMatrix.
	/// </summary>
	public IEnumerable<(MatrixCell<T>? MatrixCell, MatrixCell<T2> SubMatrixCell)> MatchCellsOnlySubMatrix<T2>(Position subMatrixPosition, Matrix<T2> subMatrix)
	{
		var workingBounds = new Bounds(subMatrixPosition, subMatrix.Size);
		return MatchCells(workingBounds, subMatrixPosition, subMatrix).Select(z => (z.MatrixCell, z.SubMatrixCell.Value));
	}

	/// <summary>
	/// OutOfBoundsRule.Ignore is assumed. Aligns the provided subMatrix with this Matrix and returns an array of pairs of cells.
	/// </summary>
	/// <param name="workingBounds">The bounds within which cells will be searched</param>
	public IEnumerable<(MatrixCell<T>? MatrixCell, MatrixCell<T2>? SubMatrixCell)> MatchCells<T2>(Bounds workingBounds, Position subMatrixPosition, Matrix<T2> subMatrix)
	{
		workingBounds = workingBounds.Positify();

		for (var boundsX = workingBounds.Position.X; boundsX < workingBounds.Position.X + workingBounds.Size.Width; boundsX++)
		{
			for (var boundsY = workingBounds.Position.Y; boundsY < workingBounds.Position.Y + workingBounds.Size.Height; boundsY++)
			{
				var thisCell = this.GetCell(boundsX, boundsY, OutOfBoundsRule.Ignore);
				var subMatrixCell = subMatrix.GetCell(boundsX - subMatrixPosition.X, boundsY - subMatrixPosition.Y, OutOfBoundsRule.Ignore);

				if (thisCell != null || subMatrixCell != null)
					yield return (thisCell, subMatrixCell);
			}
		}
	}

	public bool IncludesCompletely(Bounds bounds)
	{
		return IncludesAtLeastPartially(bounds) && Matrix.GetBoundsUnion(new Bounds(0, 0, this.Size), bounds).Size.Total == this.Size.Total;
	}

	public bool IncludesAtLeastPartially(Bounds bounds)
	{
		return Matrix.GetBoundsIntersection(new Bounds(0, 0, this.Size), bounds).Size.Total > 0;
	}

	public bool IsOutOfBounds(Position position) { return IsOutOfBounds(position.X, position.Y); }
	public bool IsOutOfBounds(int x, int y)
	{
		return x < 0 || x >= this.Width || y < 0 || y >= this.Height;
	}

	#endregion

	#region Misc Tools

	/// <summary>
	/// Returns a copy of this matrix, with the exact same values.
	/// </summary>
	public Matrix<T> Duplicate() { return GetSubmatrix(new Bounds(0, 0, this.Size)); }

	/// <summary>
	/// Returns a new Matrix that has been rotated 90 degrees clockwise.
	/// </summary>
	/// <param name="times">Number of rotations to execute. Values over 3 will circle back to 0.</param>
	public Matrix<T> Rotate90Clockwise(int times = 1)
	{
		if (times < 0)
			throw new InvalidOperationException("Cannot rotate a negative amount of times.");

		times %= 4;

		var result = this;
		for (var i = 0; i < times; i++)
		{
			result = result.Rotate90Clockwise();
		}
		return result;
	}
	private Matrix<T> Rotate90Clockwise()
	{
		var result = new Matrix<T>(this.Height, this.Width);

		for (var x = 0; x < this.Width; x++)
		{
			for (var y = 0; y < this.Height; y++)
			{
				var newX = result.Width - 1 - y;
				var newY = x;

				result[newX, newY] = this[x, y];
			}
		}

		return result;
	}

	/// <summary>
	/// Returns a new Matrix where the X positions are flipped.
	/// </summary>
	public Matrix<T> FlipX()
	{
		var result = new Matrix<T>(this.Width, this.Height);

		for (var x = 0; x < this.Width; x++)
		{
			for (var y = 0; y < this.Height; y++)
			{
				result[x, y] = this[this.Width - 1 - x, y];
			}
		}

		return result;
	}
	/// <summary>
	/// Returns a new Matrix where the Y positions are flipped.
	/// </summary>
	public Matrix<T> FlipY()
	{
		var result = new Matrix<T>(this.Width, this.Height);

		for (var x = 0; x < this.Width; x++)
		{
			for (var y = 0; y < this.Height; y++)
			{
				result[x, y] = this[x, this.Height - 1 - y];
			}
		}

		return result;
	}

	public string ToConsoleString(Func<MatrixCell<T>, string> cellDisplayer, string colSeparator)
	{
		var toString = "";

		for (var y = 0; y < Size.Height; y++)
		{
			for (var x = 0; x < Size.Width; x++)
			{
				var val = GetCell(x, y, OutOfBoundsRule.IncludeNull);
				if (cellDisplayer != null)
					toString += cellDisplayer(val.Value);
				else
					toString += val != null ? val.ToString() : string.Empty;

				if (x < Size.Width - 1 && colSeparator != null)
					toString += colSeparator;
			}
			if (y < Size.Height - 1)
				toString += "\r\n";
		}

		return toString;
	}

	#endregion

	#region IEnumerable implementation

	private IEnumerator<MatrixCell<T>> GetPrivateEnumerator()
	{
		var transformed = new List<MatrixCell<T>>();

		for (var x = 0; x < this.Width; x++)
		{
			for (var y = 0; y < this.Height; y++)
			{
				transformed.Add(this.GetCell(x, y).Value);
			}
		}

		return transformed.GetEnumerator();
	}
	public IEnumerator GetEnumerator()
	{
		return GetPrivateEnumerator();
	}
	IEnumerator<MatrixCell<T>> IEnumerable<MatrixCell<T>>.GetEnumerator()
	{
		return GetPrivateEnumerator();
	}

	#endregion
}

public class Submatrix<T> : Matrix<T>
{
	protected Position _position;
	public override Position Position => _position;
	public Bounds Bounds => new Bounds(Position, Size);


	public Submatrix() : this(new Bounds()) { }
	public Submatrix(Bounds bounds)
	{
		_position = bounds.Position;
		Grid = new T[bounds.Size.Width, bounds.Size.Height];
	}
	public Submatrix(Bounds bounds, T defaultValue = default(T)) : this(bounds)
	{
		foreach (var cell in this.ToList())
		{
			this[cell.Position] = defaultValue;
		}
	}

	public void SetPosition(Position position)
	{
		_position = position;
	}
}

/// <summary>
/// Static class containing helpers for Matrix<>
/// </summary>
public static class Matrix
{
	public static Matrix<T> FromColumn<T>(T[] column)
	{
		var result = new Matrix<T>(1, column.Length);
		for (var y = 0; y < column.Length; y++)
		{
			result[0, y] = column[y];
		}
		return result;
	}
	public static Matrix<T> FromRow<T>(T[] row)
	{
		var result = new Matrix<T>(row.Length, 1);
		for (var x = 0; x < row.Length; x++)
		{
			result[x, 0] = row[x];
		}
		return result;
	}

	/// <summary>
	/// Returns the intersection of two bounds (a.k.a. the largest bounds that can fit inside both provided bounds). If there is no intersection, returned bounds are 0,0,0,0.
	/// </summary>
	public static Bounds GetBoundsIntersection(Bounds bounds1, Bounds bounds2)
	{
		bounds1 = bounds1.Positify();
		bounds2 = bounds2.Positify();

		var result = new Bounds()
		{
			StartPoint = new Position(new[] { bounds1.StartPoint.X, bounds2.StartPoint.X }.Max(), new[] { bounds1.StartPoint.Y, bounds2.StartPoint.Y }.Max()),
			EndPoint = new Position(new[] { bounds1.EndPoint.X, bounds2.EndPoint.X }.Min(), new[] { bounds1.EndPoint.Y, bounds2.EndPoint.Y }.Min())
		};

		if (result.Size.Total == 0 || result.Size.IsInverted)
			result = new Bounds();

		return result;
	}

	/// <summary>
	/// Returns the union of two bounds (a.k.a. the smallest bounds that contain both provided bounds)
	/// </summary>
	public static Bounds GetBoundsUnion(Bounds bounds1, Bounds bounds2)
	{
		bounds1 = bounds1.Positify();
		bounds2 = bounds2.Positify();

		return new Bounds()
		{
			StartPoint = new Position(new[] { bounds1.StartPoint.X, bounds2.StartPoint.X }.Min(), new[] { bounds1.StartPoint.Y, bounds2.StartPoint.Y }.Min()),
			EndPoint = new Position(new[] { bounds1.EndPoint.X, bounds2.EndPoint.X }.Max(), new[] { bounds1.EndPoint.Y, bounds2.EndPoint.Y }.Max())
		};
	}

	/// <summary>
	/// Returns the bounds that contains the provided cells. If the array is empty, returned bounds are 0,0,0,0.
	/// </summary>
	public static Bounds GetBoundsFromCells<T>(IEnumerable<MatrixCell<T>> cells)
	{
		return GetBoundsFromPositions(cells.Select(z => z.Position));
	}

	/// <summary>
	/// Returns the bounds that contains the provided positions. If the array is empty, returned bounds are 0,0,0,0.
	/// </summary>
	public static Bounds GetBoundsFromPositions(IEnumerable<Position> positions)
	{
		if (!positions.Any())
			return new Bounds();

		return new Bounds
		{
			StartPoint = new Position(positions.Min(z => z.X), positions.Min(z => z.Y)),
			EndPoint = new Position(positions.Max(z => z.X) + 1, positions.Max(z => z.Y) + 1),
		};
	}

	public static IEnumerable<Position> PositionsWhereBoundsIncludeSubmatrix(Bounds workingBounds, Size subMatrixSize)
	{
		for (var x = workingBounds.Position.X - subMatrixSize.Width + 1; x <= workingBounds.EndPoint.X; x++)
		{
			for (var y = workingBounds.Position.Y - subMatrixSize.Height + 1; y < workingBounds.EndPoint.Y; y++)
			{
				yield return new Position(x, y);
			}
		}
	}
}

/// <summary>
/// Readonly struct that represents a piece of data and its position inside a Matrix.
/// </summary>
/// <typeparam name="T"></typeparam>
public struct MatrixCell<T>
{
	public readonly Matrix<T> Parent { get; }
	public readonly Position Position { get; }
	public readonly T Value { get; }

	/// <summary>
	/// If the cell is part of a Submatrix, will return its position relative to the parent Matrix.
	/// </summary>
	public readonly Position MasterPosition => Parent.Position + Position;

	public MatrixCell(Matrix<T> parent, Position position, T value)
	{
		Parent = parent;
		Position = position;
		Value = value;
	}

	public Position TopNeighborPosition => new Position(Position.X, Position.Y - 1);
	public Position BottomNeighborPosition => new Position(Position.X, Position.Y + 1);
	public Position LeftNeighborPosition => new Position(Position.X - 1, Position.Y);
	public Position RightNeighborPosition => new Position(Position.X + 1, Position.Y);
	public Position TopLeftNeighborPosition => new Position(Position.X - 1, Position.Y - 1);
	public Position TopRightNeighborPosition => new Position(Position.X + 1, Position.Y - 1);
	public Position BottomLeftNeighborPosition => new Position(Position.X - 1, Position.Y + 1);
	public Position BottomRightNeighborPosition => new Position(Position.X + 1, Position.Y + 1);

	public MatrixCell<T>? GetTopNeighbor(OutOfBoundsRule outOfBoundsRule = Matrix<T>.DefaultOutOfBoundsRule)
	{
		return Parent.GetCell(TopNeighborPosition, outOfBoundsRule);
	}
	public MatrixCell<T>? GetBottomNeighbor(OutOfBoundsRule outOfBoundsRule = Matrix<T>.DefaultOutOfBoundsRule)
	{
		return Parent.GetCell(BottomNeighborPosition, outOfBoundsRule);
	}
	public MatrixCell<T>? GetLeftNeighbor(OutOfBoundsRule outOfBoundsRule = Matrix<T>.DefaultOutOfBoundsRule)
	{
		return Parent.GetCell(LeftNeighborPosition, outOfBoundsRule);
	}
	public MatrixCell<T>? GetRightNeighbor(OutOfBoundsRule outOfBoundsRule = Matrix<T>.DefaultOutOfBoundsRule)
	{
		return Parent.GetCell(RightNeighborPosition, outOfBoundsRule);
	}
	public MatrixCell<T>? GetTopLeftNeighbor(OutOfBoundsRule outOfBoundsRule = Matrix<T>.DefaultOutOfBoundsRule)
	{
		return Parent.GetCell(TopLeftNeighborPosition, outOfBoundsRule);
	}
	public MatrixCell<T>? GetTopRightNeighbor(OutOfBoundsRule outOfBoundsRule = Matrix<T>.DefaultOutOfBoundsRule)
	{
		return Parent.GetCell(TopRightNeighborPosition, outOfBoundsRule);
	}
	public MatrixCell<T>? GetBottomLeftNeighbor(OutOfBoundsRule outOfBoundsRule = Matrix<T>.DefaultOutOfBoundsRule)
	{
		return Parent.GetCell(BottomLeftNeighborPosition, outOfBoundsRule);
	}
	public MatrixCell<T>? GetBottomRightNeighbor(OutOfBoundsRule outOfBoundsRule = Matrix<T>.DefaultOutOfBoundsRule)
	{
		return Parent.GetCell(BottomRightNeighborPosition, outOfBoundsRule);
	}

	/// <summary>
	/// Returns an array of the top, left, right and bottom neighbors.
	/// </summary>
	public MatrixCell<T>[] GetImmediateNeighbors(OutOfBoundsRule outOfBoundsRule = Matrix<T>.DefaultOutOfBoundsRule)
	{
		return new List<MatrixCell<T>?>
		{
			GetTopNeighbor(outOfBoundsRule),
			GetLeftNeighbor(outOfBoundsRule),
			GetRightNeighbor(outOfBoundsRule),
			GetBottomNeighbor(outOfBoundsRule),
		}.Where(z => z.HasValue).Select(z => z.Value).ToArray();
	}
	/// <summary>
	/// Returns an array of the top left, top right, bottom left and bottom right neighbors.
	/// </summary>
	public MatrixCell<T>[] GetDiagonalNeighbors(OutOfBoundsRule outOfBoundsRule = Matrix<T>.DefaultOutOfBoundsRule)
	{
		return new List<MatrixCell<T>?>
		{
			GetTopLeftNeighbor(outOfBoundsRule),
			GetTopRightNeighbor(outOfBoundsRule),
			GetBottomLeftNeighbor(outOfBoundsRule),
			GetBottomRightNeighbor(outOfBoundsRule),
		}.Where(z => z.HasValue).Select(z => z.Value).ToArray();
	}
	/// <summary>
	/// Returns an array of up to 8 cells surrounding this cell.
	/// </summary>
	public MatrixCell<T>[] GetSurroundingNeighbors(OutOfBoundsRule outOfBoundsRule = Matrix<T>.DefaultOutOfBoundsRule)
	{
		return new List<MatrixCell<T>?>
		{
			GetTopLeftNeighbor(outOfBoundsRule),
			GetTopNeighbor(outOfBoundsRule),
			GetTopRightNeighbor(outOfBoundsRule),
			GetLeftNeighbor(outOfBoundsRule),
			GetRightNeighbor(outOfBoundsRule),
			GetBottomLeftNeighbor(outOfBoundsRule),
			GetBottomNeighbor(outOfBoundsRule),
			GetBottomRightNeighbor(outOfBoundsRule),
		}.Where(z => z.HasValue).Select(z => z.Value).ToArray();
	}

	public MatrixCell<T>[] GetDiagonalNeighborsWhereAllSharedImmediates(Func<MatrixCell<T>, bool> predicate, OutOfBoundsRule outOfBoundsRule = Matrix<T>.DefaultOutOfBoundsRule)
	{
		var immediateNeighbors = GetImmediateNeighbors(outOfBoundsRule);
		return GetDiagonalNeighbors(outOfBoundsRule)
			.Where(z => z
				.GetImmediateNeighbors(OutOfBoundsRule.Ignore)
				.Where(zz => immediateNeighbors.Contains(zz))
				.All(predicate))
			.ToArray();
	}

	public MatrixCell<T>? GetOppositeNeighbor(MatrixCell<T> neighbor, OutOfBoundsRule outOfBoundsRule = Matrix<T>.DefaultOutOfBoundsRule)
	{
		if (neighbor.Position == TopNeighborPosition)
			return GetBottomNeighbor(outOfBoundsRule);
		if (neighbor.Position == BottomNeighborPosition)
			return GetTopNeighbor(outOfBoundsRule);
		if (neighbor.Position == LeftNeighborPosition)
			return GetRightNeighbor(outOfBoundsRule);
		if (neighbor.Position == RightNeighborPosition)
			return GetLeftNeighbor(outOfBoundsRule);
		if (neighbor.Position == TopLeftNeighborPosition)
			return GetBottomRightNeighbor(outOfBoundsRule);
		if (neighbor.Position == TopRightNeighborPosition)
			return GetBottomLeftNeighbor(outOfBoundsRule);
		if (neighbor.Position == BottomLeftNeighborPosition)
			return GetTopRightNeighbor(outOfBoundsRule);
		if (neighbor.Position == BottomRightNeighborPosition)
			return GetTopLeftNeighbor(outOfBoundsRule);
		return null;
	}

	/// <summary>
	/// Returns a subMatrix of this cell's Matrix. The square's size is equal to (distance * 2) + 1. For example, a distance of 2 gives a 5x5 square.
	/// </summary>
	public Matrix<T> GetSquareNeighborhood(int distance, OutOfBoundsRule outOfBoundsRule = Matrix<T>.DefaultOutOfBoundsRule)
	{
		return Parent.GetSubmatrix(new Bounds(Position.X - distance, Position.Y - distance, (distance * 2) + 1, (distance * 2) + 1), outOfBoundsRule);
	}
	public Matrix<T> Get3x3Neighborhood(OutOfBoundsRule outOfBoundsRule = Matrix<T>.DefaultOutOfBoundsRule)
	{
		return GetSquareNeighborhood(1, outOfBoundsRule);
	}
	public MatrixCell<T>[] GetColumn(OutOfBoundsRule outOfBoundsRule = Matrix<T>.DefaultOutOfBoundsRule)
	{
		return Parent.GetColumn(Position, outOfBoundsRule);
	}
	public MatrixCell<T>[] GetRow(OutOfBoundsRule outOfBoundsRule = Matrix<T>.DefaultOutOfBoundsRule)
	{
		return Parent.GetRow(Position, outOfBoundsRule);
	}

	public bool IsOnEdge => Position.X == 0 || Position.X == (Parent.Width - 1) || Position.Y == 0 || Position.Y == (Parent.Height - 1);
	public int EdgeDistance => IsOnEdge ? 0 : Mathf.Min(Mathf.Abs(Position.X), Mathf.Abs(Parent.Size.Width - 1 - Position.X), Mathf.Abs(Position.Y), Mathf.Abs(Parent.Size.Height - 1 - Position.Y));
	public bool IsOnCorner => IsOnEdge &&
		((Position.X == 0 && Position.Y == 0) ||
		(Position.X == 0 && Position.Y == Parent.Height - 1) ||
		(Position.X == Parent.Width - 1 && Position.Y == 0) ||
		(Position.X == Parent.Width - 1 && Position.Y == Parent.Height - 1));

	public static bool operator ==(MatrixCell<T> c1, MatrixCell<T> c2)
	{
		return c1.Equals(c2);
	}

	public static bool operator !=(MatrixCell<T> c1, MatrixCell<T> c2)
	{
		return !c1.Equals(c2);
	}

	public override bool Equals(object obj)
	{
		return obj is MatrixCell<T> cell &&
			   ReferenceEquals(Parent, cell.Parent) &&
			   EqualityComparer<Position>.Default.Equals(Position, cell.Position) &&
			   EqualityComparer<T>.Default.Equals(Value, cell.Value);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Parent, Position, Value);
	}
}

public struct Position
{
	public int X { get; set; }
	public int Y { get; set; }

	public Position(int x, int y)
	{
		X = x;
		Y = y;
	}

	public Position Flip()
	{
		return new Position(Y, X);
	}

	public int DistanceTo(Position position)
	{
		return Mathf.Abs(X - position.X) + Mathf.Abs(Y - position.Y);
	}

	public static bool operator ==(Position p1, Position p2)
	{
		return p1.Equals(p2);
	}

	public static bool operator !=(Position p1, Position p2)
	{
		return !p1.Equals(p2);
	}

	public static Position operator +(Position p1, Position p2)
	{
		return new Position(p1.X + p2.X, p1.Y + p2.Y);
	}

	public static Position operator -(Position p1, Position p2)
	{
		return new Position(p1.X - p2.X, p1.Y - p2.Y);
	}

	public override bool Equals(object obj)
	{
		return obj is Position position &&
			   X == position.X &&
			   Y == position.Y;
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(X, Y);
	}

	public override string ToString()
	{
		return $"X:{X} Y:{Y}";
	}
}

public struct Size
{
	public int Width { get; set; }
	public int Height { get; set; }

	public int Total => Math.Abs(Width * Height);
	public bool IsSquare => Width == Height;
	public bool IsWidthInverted => Width < 0;
	public bool IsHeightInverted => Height < 0;
	public bool IsInverted => IsWidthInverted || IsHeightInverted;

	public Size Flip()
	{
		return new Size(Height, Width);
	}

	public Size(int width, int height)
	{
		Width = width;
		Height = height;
	}

	public static bool operator ==(Size s1, Size s2)
	{
		return s1.Equals(s2);
	}

	public static bool operator !=(Size s1, Size s2)
	{
		return !s1.Equals(s2);
	}

	public override bool Equals(object obj)
	{
		return obj is Size size &&
			   Width == size.Width &&
			   Height == size.Height;
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Width, Height);
	}

	public override string ToString()
	{
		return $"Width:{Width} Height:{Height}";
	}
}

public struct Bounds
{
	public Position Position { get; set; }
	public Size Size { get; set; }

	public Bounds(int xPos, int yPos, int width, int height) : this(new Position(xPos, yPos), new Size(width, height)) { }
	public Bounds(Position position, int width, int height) : this(position, new Size(width, height)) { }
	public Bounds(int xPos, int yPos, Size size) : this(new Position(xPos, yPos), size) { }
	public Bounds(Position position, Size size)
	{
		Position = position;
		Size = size;
	}

	/// <summary>
	/// Not representative of which cell is included.
	/// </summary>
	public Position StartPoint
	{
		get { return Position; }
		set
		{
			var xDif = value.X - StartPoint.X;
			var yDif = value.Y - StartPoint.Y;
			Position = value;
			Size = new Size(Size.Width - xDif, Size.Height - yDif);
		}
	}
	/// <summary>
	/// Not representative of which cell is included.
	/// </summary>
	public Position EndPoint
	{
		get { return new Position(Position.X + Size.Width, Position.Y + Size.Height); }
		set
		{
			var xDif = value.X - EndPoint.X;
			var yDif = value.Y - EndPoint.Y;
			Size = new Size(Size.Width + xDif, Size.Height + yDif);
		}
	}

	/// <summary>
	/// If the Size has negative values, this method puts both in positive and adjusts the Position (starting point) to the top left corner.
	/// </summary>
	public Bounds Positify()
	{
		var result = this;

		if (Size.IsWidthInverted)
			result = result.FlipX();
		if (Size.IsHeightInverted)
			result = result.FlipY();

		return result;
	}

	public Bounds FlipX()
	{
		return new Bounds(new Position(Position.X + Size.Width, Position.Y), new Size(Size.Width * -1, Size.Height));
	}

	public Bounds FlipY()
	{
		return new Bounds(new Position(Position.X, Position.Y + Size.Height), new Size(Size.Width, Size.Height * -1));
	}

	public override bool Equals(object obj)
	{
		return obj is Bounds bounds &&
			   EqualityComparer<Position>.Default.Equals(Position, bounds.Position) &&
			   EqualityComparer<Size>.Default.Equals(Size, bounds.Size);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Position, Size);
	}

	public static bool operator ==(Bounds b1, Bounds b2)
	{
		return b1.Equals(b2);
	}

	public static bool operator !=(Bounds b1, Bounds b2)
	{
		return !b1.Equals(b2);
	}

	public override string ToString()
	{
		return $"{Position} , {Size}";
	}
}

public enum OutOfBoundsRule
{
	/// <summary>
	/// Normal procedure : will throw exceptions when out of bound indexes are given
	/// </summary>
	Exception,
	/// <summary>
	/// If a position is out of bounds, it will be ignored from the operation / cut from the results.
	/// </summary>
	Ignore,
	/// <summary>
	/// Assumes the position exists, and gives a null value if out of bounds.
	/// </summary>
	IncludeNull,
}