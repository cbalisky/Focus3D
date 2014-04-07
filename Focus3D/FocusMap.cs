using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Focus3D
{
    class FocusMap
    {
        private uint[] _map;
        public uint[] Map
        {
            get
            {
                return _map;
            }
            set
            {
                if (value.Length == this.length)
                    value.CopyTo(_map, 0);
            }
        }
        private Trigger[] _triggered;
        public Trigger[] Triggers
        {
            get
            {
                return _triggered;
            }
            set
            {
                if (value.Length == this.length)
                    value.CopyTo(_triggered, 0);
            }
        }
        private int _regionsX;
        private int _regionsY;

        private int _width;
        public int width
        {
            get
            {
                return _width;
            }
        }
        private int _height;
        public int height
        {
            get
            {
                return _height;
            }
        }
        protected int length;
        public int sampleRegion;
        public int threshold;
        public enum Trigger {Unseen = 0, Triggered, Drawn };
        public FocusMap(int x, int y)
        {
            _map = new uint[x * y];
            _width = x;
            _height = y;
            _triggered = new Trigger[x * y];
            for (int i = 0; i < _triggered.Length; i++)
                _triggered[i] = Trigger.Unseen;
            _regionsX = x;
            _regionsY = y;
            length = _map.Length;
            sampleRegion = 3;
        }

        public static bool[] operator > (FocusMap A, FocusMap B) 
        {
            bool[] map;
            if (A._regionsY == B._regionsY && A._regionsX == B._regionsX)
            {
                int bSize = A.sampleRegion;
                int mid = bSize / 2 + 1;
                uint[] bufferA = new uint[(A.width + bSize - 1) * bSize];
                uint[] bufferB = new uint[(A.width + bSize - 1) * bSize];
                int[] bufRow = new int[bSize];
                //DON'T DO THIS HERE, CALCULATE IT AHEAD OF TIME FOR EACH SET
                for (int i = 0; i < bSize - 1; i++)
                {
                    copyRow(ref bufferA, A.Map, i - mid, i, A.width, bSize);
                    copyRow(ref bufferB, B.Map, i - mid, i, B.width, bSize);
                }
                uint sumA, sumB;
                int edge = bSize / 2;
                int sampleArea = bSize * bSize;
                int x, y;
                map = new bool[A._regionsY * A._regionsX];
                for (y = 0; y < A.height; y++)
                {
                    for (int i = 0; i < bSize; i++)
                        bufRow[i] = ((y + i) % bSize);
                    copyRow(ref bufferA, A.Map, y + edge, bufRow[bSize-1], A.width, bSize);
                    copyRow(ref bufferB, B.Map, y + edge, bufRow[bSize - 1], B.width, bSize);
                    sumA = sumB = 0;
                    for (int j = 0; j < bSize; j++)
                    {
                        for (int k = 0; k < bSize; k++)
                        {
                            sumA += bufferA[bufRow[j] * A.width + k];
                            sumB += bufferB[bufRow[j] * A.width + k];
                        }
                        
                    }
                    for (x = 0; x < A.width; x++)
                    {
                        map[y * A.width + x] = sumA / sampleArea - sumB / sampleArea >= A.threshold;// sumB / sampleArea / 10;
                        for (int k = 0; k < bSize; k++)
                        {
                            sumA += bufferA[bufRow[k] * A.width + x + bSize] - bufferA[bufRow[k] * A.width + x];
                            sumB += bufferB[bufRow[k] * A.width + x + bSize] - bufferB[bufRow[k] * A.width + x];
                        }
                    }
                }
                /*for (i = 0; i < A.length; i++)
                    map[i] = A.Map[i] - B.Map[i] > 10;*/
            }
            else
                map = new bool[1] {false};
            return map;
        }

        private static void copyRow(ref uint[] buf, uint[] src, int row, int bufRow, int width, int sample)
        {
            int numRows = src.Length / width;
            int edge = sample / 2;
            int bufW = width + sample - 1;
            int effRow;

            //copy row
            effRow = Math.Min(Math.Max(row, 0), numRows - 1);
            for (int j = 0; j < edge; j++)
            {
                buf[bufRow * bufW + j] = src[effRow * width];
                uint temp = src[effRow * width + width - 1];
                buf[bufRow * bufW + bufW - 1 - j] = temp;
            }
            for (int k = 0; k < width; k++)
                buf[bufRow * bufW + edge + k] = src[effRow * width + k];
            
        }

        private static void copyRows(ref short[] buf, short[] src, int row, int width, int sample)
        {
            int numRows = src.Length / width;
            int edge = sample / 2;
            int bufW = width + sample - 1;
            int effRow;

            //copy preceding rows
            for (int i = 0; i < edge; i++)
            {
                effRow = Math.Max(row - edge + i, 0);
                for (int j = 0; j < edge; j++)
                {
                    buf[effRow * bufW + j] = src[effRow * width];
                    buf[effRow * bufW + bufW - 1 - j] = src[effRow * width + width - 1];
                }
                for (int k = 0; k < width; k++)
                    buf[effRow * bufW + edge + k] = src[effRow * width + k];


            }
            //copy center row
            for (int j = 0; j < edge; j++)
            {
                buf[row * bufW + j] = src[row * width];
                buf[row * bufW + bufW - 1 - j] = src[row * width + width - 1];
            }
            for (int k = 0; k < width; k++)
                buf[row * bufW + edge + k] = src[row * width + k];

            //copy following rows
            for (int i = 0; i < edge; i++)
            {
                effRow = Math.Min(row + edge - i, numRows - 1);
                for (int j = 0; j < edge; j++)
                {
                    buf[effRow * bufW + j] = src[effRow * width];
                    buf[effRow * bufW + bufW - 1 - j] = src[effRow * width + width - 1];
                }
                for (int k = 0; k < width; k++)
                    buf[effRow * bufW + edge + k] = src[effRow * width + k];


            }
        }

        public static bool[] operator < (FocusMap A, FocusMap B)
        {
            bool[] map;
            if (A._regionsY == B._regionsY && A._regionsX == B._regionsX)
            {
                map = new bool[A._regionsY * A._regionsX];
                for (int i = 0; i < A.length; i++)
                    map[i] = Math.Abs(A.Map[i] - B.Map[i]) < Math.Abs(A.Map[i] / 2);
            }
            else
                map = new bool[1] {false};
            return map;
        }
    }
}
