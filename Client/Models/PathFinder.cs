using Client.Scenes;
using Client.Scenes.Views;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Client.Models
{
    public class Heap<T> where T : IHeapItem<T>
    {
        private T[] items;
        private int currentItemCount;

        public Heap(int maxHeapSize)
        {
            items = new T[Math.Max(1, maxHeapSize)];
        }

        public void Add(T item)
        {
            if (currentItemCount >= items.Length)
                throw new InvalidOperationException("Heap capacity exceeded.");

            item.HeapIndex = currentItemCount;
            items[currentItemCount] = item;
            SortUp(item);
            currentItemCount++;
        }

        public T RemoveFirst()
        {
            if (currentItemCount == 0)
                throw new InvalidOperationException("Cannot remove an item from an empty heap.");

            T firstItem = items[0];
            currentItemCount--;

            items[0] = items[currentItemCount];
            items[currentItemCount] = default;

            if (currentItemCount > 0)
            {
                items[0].HeapIndex = 0;
                SortDown(items[0]);
            }

            firstItem.HeapIndex = -1;
            return firstItem;
        }

        public void UpdateItem(T item)
        {
            SortUp(item);
        }

        public int Count { get { return currentItemCount; } }

        public bool Contains(T item)
        {
            if (ReferenceEquals(item, null) || item.HeapIndex < 0 || item.HeapIndex >= currentItemCount) return false;
            return Equals(items[item.HeapIndex], item);
        }

        private void SortDown(T item)
        {
            while (true)
            {
                int childIndexLeft = (item.HeapIndex * 2) + 1;
                int childIndexRight = (item.HeapIndex * 2) + 2;
                int swapIndex = 0;

                if (childIndexLeft < currentItemCount)
                {
                    swapIndex = childIndexLeft;
                    if (childIndexRight < currentItemCount)
                    {
                        if (items[childIndexLeft].CompareTo(items[childIndexRight]) < 0)
                        {
                            swapIndex = childIndexRight;
                        }
                    }

                    if (item.CompareTo(items[swapIndex]) < 0)
                    {
                        Swap(item, items[swapIndex]);
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    return;
                }
            }
        }

        private void SortUp(T item)
        {
            while (item.HeapIndex > 0)
            {
                int parentIndex = (item.HeapIndex - 1) / 2;
                T parentItem = items[parentIndex];
                if (item.CompareTo(parentItem) > 0)
                {
                    Swap(item, parentItem);
                }
                else
                    break;
            }
        }

        private void Swap(T itemA, T itemB)
        {
            items[itemA.HeapIndex] = itemB;
            items[itemB.HeapIndex] = itemA;

            int itemAIndex = itemA.HeapIndex;
            itemA.HeapIndex = itemB.HeapIndex;
            itemB.HeapIndex = itemAIndex;
        }
    }

    public interface IHeapItem<T> : IComparable<T>
    {
        int HeapIndex
        {
            get; set;
        }
    }

    public class Node : IHeapItem<Node>
    {
        public bool Walkable
        {
            get { return Map.EmptyCell(Location); }
        }

        public MapControl Map;
        public Point Location;
        public Node Parent;

        public int GCost, HCost;
        public int SearchID;

        public int FCost
        {
            get { return GCost + HCost; }
        }

        private int _heapIndex;

        public int HeapIndex
        {
            get { return _heapIndex; }
            set { _heapIndex = value; }
        }

        public int CompareTo(Node nodeToCompare)
        {
            int compare = FCost.CompareTo(nodeToCompare.FCost);
            if (compare == 0)
            {
                compare = HCost.CompareTo(nodeToCompare.HCost);
            }
            return -compare;
        }

        public Node(MapControl map, int x, int y)
        {
            Map = map;
            Location = new Point(x, y);
            HeapIndex = -1;
            GCost = int.MaxValue;
        }
    }

    public class PathFinder
    {
        private static readonly Point InvalidLocation = new Point(-1, -1);

        private Node[,] Grid;
        private int _searchID;

        public MapControl Map;

        public int MaxSize
        {
            get { return Grid == null ? 0 : Grid.GetLength(0) * Grid.GetLength(1); }
        }

        public PathFinder(MapControl map)
        {
            Map = map;

            CreateGrid();
        }

        public bool Matches(MapControl map)
        {
            int width = GetSafeWidth(map);
            int height = GetSafeHeight(map);

            return map != null &&
                   ReferenceEquals(Map, map) &&
                   Grid != null &&
                   Grid.GetLength(0) == width &&
                   Grid.GetLength(1) == height;
        }

        private void CreateGrid()
        {
            int width = GetSafeWidth(Map);
            int height = GetSafeHeight(Map);

            Grid = new Node[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Grid[x, y] = new Node(Map, x, y);
                }
            }
        }

        public Point GetRandomLocation(Point location, int range, int attempts = 25)
        {
            int width = GetSafeWidth(Map);
            int height = GetSafeHeight(Map);

            if (width <= 0 || height <= 0) return InvalidLocation;

            int minX = Math.Max(0, location.X - range);
            int maxX = Math.Min(width - 1, location.X + range);
            int minY = Math.Max(0, location.Y - range);
            int maxY = Math.Min(height - 1, location.Y + range);

            if (minX > maxX || minY > maxY) return InvalidLocation;

            for (int i = 0; i < attempts; i++)
            {
                Point test = new Point(Envir.CEnvir.Random.Next(minX, maxX + 1), Envir.CEnvir.Random.Next(minY, maxY + 1));

                if (Map.EmptyCell(test))
                    return test;
            }

            return InvalidLocation;
        }

        private static int GetSafeWidth(MapControl map)
        {
            if (map?.Cells == null || map.Width <= 0) return 0;

            return Math.Min(map.Width, map.Cells.GetLength(0));
        }

        private static int GetSafeHeight(MapControl map)
        {
            if (map?.Cells == null || map.Height <= 0) return 0;

            return Math.Min(map.Height, map.Cells.GetLength(1));
        }

        public List<Node> FindPath(Point start, Point target, int MaxNodes = 0)
        {
            if (MaxSize <= 0 || !IsInsideMap(start) || !IsInsideMap(target)) return null;

            BeginSearch();

            Node startNode = GetNode(start);
            Node targetNode = GetNode(target);

            Heap<Node> openSet = new Heap<Node>(MaxSize);
            HashSet<Node> closedSet = new HashSet<Node>();

            PrepareNode(startNode);
            startNode.GCost = 0;
            startNode.HCost = GetDistance(startNode, targetNode);
            startNode.Parent = null;

            openSet.Add(startNode);

            while (openSet.Count > 0)
            {
                Node currentNode = openSet.RemoveFirst();

                closedSet.Add(currentNode);

                if (currentNode == targetNode)
                {
                    var path = RetracePath(startNode, targetNode);
                    return MaxNodes == 0 || path.Count <= MaxNodes ? path : null;
                }

                foreach (Node neighbor in GetNeighbours(currentNode))
                {
                    PrepareNode(neighbor);

                    if (!neighbor.Walkable || closedSet.Contains(neighbor))
                        continue;

                    int newMovementCostToNeighbor = currentNode.GCost + GetDistance(currentNode, neighbor);
                    bool isOpen = openSet.Contains(neighbor);
                    if (newMovementCostToNeighbor < neighbor.GCost || !isOpen)
                    {
                        neighbor.GCost = newMovementCostToNeighbor;
                        neighbor.HCost = GetDistance(neighbor, targetNode);
                        neighbor.Parent = currentNode;

                        if (!isOpen)
                            openSet.Add(neighbor);
                        else
                        {
                            openSet.UpdateItem(neighbor);
                        }
                    }
                }
            }

            return null;
        }

        private void BeginSearch()
        {
            if (_searchID == int.MaxValue)
            {
                foreach (Node node in Grid)
                    node.SearchID = 0;

                _searchID = 0;
            }

            _searchID++;
        }

        private void PrepareNode(Node node)
        {
            if (node.SearchID == _searchID) return;

            node.SearchID = _searchID;
            node.GCost = int.MaxValue;
            node.HCost = 0;
            node.Parent = null;
            node.HeapIndex = -1;
        }

        public List<Node> RetracePath(Node startNode, Node endNode)
        {
            List<Node> path = new List<Node>();
            Node currentNode = endNode;

            while (currentNode != startNode)
            {
                path.Add(currentNode);
                currentNode = currentNode.Parent;
            }

            path.Add(startNode);
            path.Reverse();

            return path;
        }

        private int GetDistance(Node nodeA, Node nodeB)
        {
            int distX = Math.Abs(nodeA.Location.X - nodeB.Location.X);
            int distY = Math.Abs(nodeA.Location.Y - nodeB.Location.Y);

            if (distX > distY)
                return 14 * distY + (10 * (distX - distY));

            return 14 * distX + (10 * (distY - distX));
        }

        private Node GetNode(Point location)
        {
            return Grid[location.X, location.Y];
        }

        private bool IsInsideMap(Point location)
        {
            return Grid != null &&
                   location.X >= 0 && location.Y >= 0 &&
                   location.X < Grid.GetLength(0) && location.Y < Grid.GetLength(1);
        }

        private List<Node> GetNeighbours(Node node)
        {
            List<Node> neighbours = new List<Node>();
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    if (x == 0 && y == 0) continue;

                    int checkX = node.Location.X + x;
                    int checkY = node.Location.Y + y;

                    if (checkX >= 0 && checkX < Grid.GetLength(0) && checkY >= 0 && checkY < Grid.GetLength(1))
                    {
                        neighbours.Add(Grid[checkX, checkY]);
                    }
                }
            }

            return neighbours;
        }

    }
}
