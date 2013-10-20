﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Drawing;

namespace SS13_Shared
{
    public class QuadTree<T> where T : class, IQuadObject
    {
        private readonly bool sort;
        private readonly SizeF minLeafSizeF;
        private readonly int maxObjectsPerLeaf;
        private QuadNode root = null;
        private Dictionary<T, QuadNode> objectToNodeLookup = new Dictionary<T, QuadNode>();
        private Dictionary<T, int> objectSortOrder = new Dictionary<T, int>();
        public QuadNode Root { get { return root; } }
        private object syncLock = new object();
        private int objectSortId = 0;

        public QuadTree(SizeF minLeafSizeF, int maxObjectsPerLeaf)
        {
            this.minLeafSizeF = minLeafSizeF;
            this.maxObjectsPerLeaf = maxObjectsPerLeaf;
        }

        public int GetSortOrder(T quadObject)
        {
            lock (objectSortOrder)
            {
                if (!objectSortOrder.ContainsKey(quadObject))
                    return -1;
                else
                {
                    return objectSortOrder[quadObject];
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="minLeafSizeF">The smallest SizeF a leaf will split into</param>
        /// <param name="maxObjectsPerLeaf">Maximum number of objects per leaf before it forces a split into sub quadrants</param>
        /// <param name="sort">Whether or not queries will return objects in the order in which they were added</param>
        public QuadTree(SizeF minLeafSizeF, int maxObjectsPerLeaf, bool sort)
            : this(minLeafSizeF, maxObjectsPerLeaf)
        {
            this.sort = sort;
        }

        public void Insert(T quadObject)
        {
            lock (syncLock)
            {
                if (sort & !objectSortOrder.ContainsKey(quadObject))
                {
                    objectSortOrder.Add(quadObject, objectSortId++);
                }

                RectangleF bounds = quadObject.Bounds;
                if (root == null)
                {
                    var rootSizeF = new SizeF((float)Math.Ceiling(bounds.Width / minLeafSizeF.Width),
                                            (float)Math.Ceiling(bounds.Height / minLeafSizeF.Height));
                    double multiplier = Math.Max(rootSizeF.Width, rootSizeF.Height);
                    rootSizeF = new SizeF((float)(minLeafSizeF.Width * multiplier), (float)(minLeafSizeF.Height * multiplier));
                    var center = new Point((int)(bounds.X + bounds.Width / 2), (int)(bounds.Y + bounds.Height / 2));
                    var rootOrigin = new Point((int)(center.X - rootSizeF.Width / 2), (int)(center.Y - rootSizeF.Height / 2));
                    root = new QuadNode(new RectangleF(rootOrigin, rootSizeF));
                }

                while (!root.Bounds.Contains(bounds))
                {
                    ExpandRoot(bounds);
                }

                InsertNodeObject(root, quadObject);
            }
        }

        public List<T> Query(RectangleF bounds)
        {
            lock (syncLock)
            {
                List<T> results = new List<T>();
                if (root != null)
                    Query(bounds, root, results);
                if (sort)
                    results.Sort((a, b) => { return objectSortOrder[a].CompareTo(objectSortOrder[b]); });
                return results;
            }
        }

        private void Query(RectangleF bounds, QuadNode node, List<T> results)
        {
            lock (syncLock)
            {
                if (node == null) return;

                if (bounds.IntersectsWith(node.Bounds))
                {
                    foreach (T quadObject in node.Objects)
                    {
                        if (bounds.IntersectsWith(quadObject.Bounds))
                            results.Add(quadObject);
                    }

                    foreach (QuadNode childNode in node.Nodes)
                    {
                        Query(bounds, childNode, results);
                    }
                }
            }
        }

        private void ExpandRoot(RectangleF newChildBounds)
        {
            lock (syncLock)
            {
                bool isNorth = root.Bounds.Y < newChildBounds.Y;
                bool isWest = root.Bounds.X < newChildBounds.X;

                DiRectangleFion rootDiRectangleFion;
                if (isNorth)
                {
                    rootDiRectangleFion = isWest ? DiRectangleFion.NW : DiRectangleFion.NE;
                }
                else
                {
                    rootDiRectangleFion = isWest ? DiRectangleFion.SW : DiRectangleFion.SE;
                }

                double newX = (rootDiRectangleFion == DiRectangleFion.NW || rootDiRectangleFion == DiRectangleFion.SW)
                                  ? root.Bounds.X
                                  : root.Bounds.X - root.Bounds.Width;
                double newY = (rootDiRectangleFion == DiRectangleFion.NW || rootDiRectangleFion == DiRectangleFion.NE)
                                  ? root.Bounds.Y
                                  : root.Bounds.Y - root.Bounds.Height;
                RectangleF newRootBounds = new RectangleF((float)newX, (float)newY, (float)root.Bounds.Width * 2f, (float)root.Bounds.Height * 2f);
                QuadNode newRoot = new QuadNode(newRootBounds);
                SetupChildNodes(newRoot);
                newRoot[rootDiRectangleFion] = root;
                root = newRoot;
            }
        }

        private void InsertNodeObject(QuadNode node, T quadObject)
        {
            lock (syncLock)
            {
                if (!node.Bounds.Contains(quadObject.Bounds))
                    throw new Exception("This should not happen, child does not fit within node bounds");

                if (!node.HasChildNodes() && node.Objects.Count + 1 > maxObjectsPerLeaf)
                {
                    SetupChildNodes(node);

                    List<T> childObjects = new List<T>(node.Objects);
                    List<T> childrenToRelocate = new List<T>();

                    foreach (T childObject in childObjects)
                    {
                        foreach (QuadNode childNode in node.Nodes)
                        {
                            if (childNode == null)
                                continue;

                            if (childNode.Bounds.Contains(childObject.Bounds))
                            {
                                childrenToRelocate.Add(childObject);
                            }
                        }
                    }

                    foreach (T childObject in childrenToRelocate)
                    {
                        RemoveQuadObjectFromNode(childObject);
                        InsertNodeObject(node, childObject);
                    }
                }

                foreach (QuadNode childNode in node.Nodes)
                {
                    if (childNode != null)
                    {
                        if (childNode.Bounds.Contains(quadObject.Bounds))
                        {
                            InsertNodeObject(childNode, quadObject);
                            return;
                        }
                    }
                }

                AddQuadObjectToNode(node, quadObject);
            }
        }

        private void ClearQuadObjectsFromNode(QuadNode node)
        {
            lock (syncLock)
            {
                List<T> quadObjects = new List<T>(node.Objects);
                foreach (T quadObject in quadObjects)
                {
                    RemoveQuadObjectFromNode(quadObject);
                }
            }
        }

        private void RemoveQuadObjectFromNode(T quadObject)
        {
            lock (syncLock)
            {
                QuadNode node = objectToNodeLookup[quadObject];
                node.quadObjects.Remove(quadObject);
                objectToNodeLookup.Remove(quadObject);
            }
        }

        private void AddQuadObjectToNode(QuadNode node, T quadObject)
        {
            lock (syncLock)
            {
                node.quadObjects.Add(quadObject);
                objectToNodeLookup.Add(quadObject, node);
            }
        }

        private void SetupChildNodes(QuadNode node)
        {
            lock (syncLock)
            {
                if (minLeafSizeF.Width <= node.Bounds.Width / 2 && minLeafSizeF.Height <= node.Bounds.Height / 2)
                {
                    node[DiRectangleFion.NW] = new QuadNode(node.Bounds.X, node.Bounds.Y, node.Bounds.Width / 2,
                                                      node.Bounds.Height / 2);
                    node[DiRectangleFion.NE] = new QuadNode(node.Bounds.X + node.Bounds.Width / 2, node.Bounds.Y,
                                                      node.Bounds.Width / 2,
                                                      node.Bounds.Height / 2);
                    node[DiRectangleFion.SW] = new QuadNode(node.Bounds.X, node.Bounds.Y + node.Bounds.Height / 2,
                                                      node.Bounds.Width / 2,
                                                      node.Bounds.Height / 2);
                    node[DiRectangleFion.SE] = new QuadNode(node.Bounds.X + node.Bounds.Width / 2,
                                                      node.Bounds.Y + node.Bounds.Height / 2,
                                                      node.Bounds.Width / 2, node.Bounds.Height / 2);

                }
            }
        }

        public void Remove(T quadObject)
        {
            lock (syncLock)
            {
                if (sort && objectSortOrder.ContainsKey(quadObject))
                {
                    objectSortOrder.Remove(quadObject);
                }

                if (!objectToNodeLookup.ContainsKey(quadObject))
                    throw new KeyNotFoundException("QuadObject not found in dictionary for removal");

                QuadNode containingNode = objectToNodeLookup[quadObject];
                RemoveQuadObjectFromNode(quadObject);

                if (containingNode.Parent != null)
                    CheckChildNodes(containingNode.Parent);
            }
        }



        private void CheckChildNodes(QuadNode node)
        {
            lock (syncLock)
            {
                if (GetQuadObjectCount(node) <= maxObjectsPerLeaf)
                {
                    // Move child objects into this node, and delete sub nodes
                    List<T> subChildObjects = GetChildObjects(node);
                    foreach (T childObject in subChildObjects)
                    {
                        if (!node.Objects.Contains(childObject))
                        {
                            RemoveQuadObjectFromNode(childObject);
                            AddQuadObjectToNode(node, childObject);
                        }
                    }
                    if (node[DiRectangleFion.NW] != null)
                    {
                        node[DiRectangleFion.NW].Parent = null;
                        node[DiRectangleFion.NW] = null;
                    }
                    if (node[DiRectangleFion.NE] != null)
                    {
                        node[DiRectangleFion.NE].Parent = null;
                        node[DiRectangleFion.NE] = null;
                    }
                    if (node[DiRectangleFion.SW] != null)
                    {
                        node[DiRectangleFion.SW].Parent = null;
                        node[DiRectangleFion.SW] = null;
                    }
                    if (node[DiRectangleFion.SE] != null)
                    {
                        node[DiRectangleFion.SE].Parent = null;
                        node[DiRectangleFion.SE] = null;
                    }

                    if (node.Parent != null)
                        CheckChildNodes(node.Parent);
                    else
                    {
                        // Its the root node, see if we're down to one quadrant, with none in local storage - if so, ditch the other three
                        int numQuadrantsWithObjects = 0;
                        QuadNode nodeWithObjects = null;
                        foreach (QuadNode childNode in node.Nodes)
                        {
                            if (childNode != null && GetQuadObjectCount(childNode) > 0)
                            {
                                numQuadrantsWithObjects++;
                                nodeWithObjects = childNode;
                                if (numQuadrantsWithObjects > 1) break;
                            }
                        }
                        if (numQuadrantsWithObjects == 1)
                        {
                            foreach (QuadNode childNode in node.Nodes)
                            {
                                if (childNode != nodeWithObjects)
                                    childNode.Parent = null;
                            }
                            root = nodeWithObjects;
                        }
                    }
                }
            }
        }


        private List<T> GetChildObjects(QuadNode node)
        {
            lock (syncLock)
            {
                List<T> results = new List<T>();
                results.AddRange(node.quadObjects);
                foreach (QuadNode childNode in node.Nodes)
                {
                    if (childNode != null)
                        results.AddRange(GetChildObjects(childNode));
                }
                return results;
            }
        }

        public int GetQuadObjectCount()
        {
            lock (syncLock)
            {
                if (root == null)
                    return 0;
                int count = GetQuadObjectCount(root);
                return count;
            }
        }

        private int GetQuadObjectCount(QuadNode node)
        {
            lock (syncLock)
            {
                int count = node.Objects.Count;
                foreach (QuadNode childNode in node.Nodes)
                {
                    if (childNode != null)
                    {
                        count += GetQuadObjectCount(childNode);
                    }
                }
                return count;
            }
        }

        public int GetQuadNodeCount()
        {
            lock (syncLock)
            {
                if (root == null)
                    return 0;
                int count = GetQuadNodeCount(root, 1);
                return count;
            }
        }

        private int GetQuadNodeCount(QuadNode node, int count)
        {
            lock (syncLock)
            {
                if (node == null) return count;

                foreach (QuadNode childNode in node.Nodes)
                {
                    if (childNode != null)
                        count++;
                }
                return count;
            }
        }

        public List<QuadNode> GetAllNodes()
        {
            lock (syncLock)
            {
                List<QuadNode> results = new List<QuadNode>();
                if (root != null)
                {
                    results.Add(root);
                    GetChildNodes(root, results);
                }
                return results;
            }
        }

        private void GetChildNodes(QuadNode node, ICollection<QuadNode> results)
        {
            lock (syncLock)
            {
                foreach (QuadNode childNode in node.Nodes)
                {
                    if (childNode != null)
                    {
                        results.Add(childNode);
                        GetChildNodes(childNode, results);
                    }
                }
            }
        }

        public class QuadNode
        {
            private static int _id = 0;
            public readonly int ID = _id++;

            public QuadNode Parent { get; internal set; }

            private QuadNode[] _nodes = new QuadNode[4];
            public QuadNode this[DiRectangleFion diRectangleFion]
            {
                get
                {
                    switch (diRectangleFion)
                    {
                        case DiRectangleFion.NW:
                            return _nodes[0];
                        case DiRectangleFion.NE:
                            return _nodes[1];
                        case DiRectangleFion.SW:
                            return _nodes[2];
                        case DiRectangleFion.SE:
                            return _nodes[3];
                        default:
                            return null;
                    }
                }
                set
                {
                    switch (diRectangleFion)
                    {
                        case DiRectangleFion.NW:
                            _nodes[0] = value;
                            break;
                        case DiRectangleFion.NE:
                            _nodes[1] = value;
                            break;
                        case DiRectangleFion.SW:
                            _nodes[2] = value;
                            break;
                        case DiRectangleFion.SE:
                            _nodes[3] = value;
                            break;
                    }
                    if (value != null)
                        value.Parent = this;
                }
            }

            public ReadOnlyCollection<QuadNode> Nodes;

            internal List<T> quadObjects = new List<T>();
            public ReadOnlyCollection<T> Objects;

            public RectangleF Bounds { get; internal set; }

            public bool HasChildNodes()
            {
                return _nodes[0] != null;
            }

            public QuadNode(RectangleF bounds)
            {
                Bounds = bounds;
                Nodes = new ReadOnlyCollection<QuadNode>(_nodes);
                Objects = new ReadOnlyCollection<T>(quadObjects);
            }

            public QuadNode(float x, float y, float width, float height)
                : this(new RectangleF(x, y, width, height))
            {

            }
        }
    }

    public enum DiRectangleFion : int
    {
        NW = 0,
        NE = 1,
        SW = 2,
        SE = 3
    }

    public interface IQuadObject
    {
        RectangleF Bounds { get; }
    }
}