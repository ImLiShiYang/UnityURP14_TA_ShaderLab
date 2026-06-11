using UnityEngine;
using UnityEngine.Rendering;

public class FastFourierTransform
    {
        private readonly int KERNEL_SCALE;
        private readonly int KERNEL_SHIFT;
        private readonly int KERNEL_HORIZONTAL_STEP;
        private readonly int KERNEL_VERTICAL_STEP;
        private readonly int KERNEL_BIT_REVERSE;
        
        private ComputeShader fftShader;
        private ComputeBuffer hBitReverseIndices;
        private ComputeBuffer vBitReverseIndices;
        private RenderTexture buffer;
        
        private Vector2Int size;

        public FastFourierTransform(ComputeShader fftShader, Vector2Int size)
        {
            this.fftShader = fftShader;

            KERNEL_SCALE = fftShader.FindKernel("Scale");
            KERNEL_SHIFT = fftShader.FindKernel("Shift");
            KERNEL_HORIZONTAL_STEP = fftShader.FindKernel("HorizontalStep");
            KERNEL_VERTICAL_STEP = fftShader.FindKernel("VerticalStep");
            KERNEL_BIT_REVERSE = fftShader.FindKernel("BitReverse");
            
            Allocate(size);
        }
        
        public void FFT2D(RenderTexture input)
        {
            Vector2Int fftGroupSize = new Vector2Int(Mathf.Max(1, size.x >> 4), Mathf.Max(1, size.y >> 4));
            Vector2Int groupSize = new Vector2Int(Mathf.Max(1, size.x >> 3), Mathf.Max(1, size.y >> 3));
            fftShader.SetTexture(KERNEL_BIT_REVERSE, P_BUFFER0, input);
            fftShader.SetTexture(KERNEL_BIT_REVERSE, P_BUFFER1, buffer);
            fftShader.Dispatch(KERNEL_BIT_REVERSE, groupSize.x, groupSize.y, 1);

            int logSize = (int)Mathf.Log(size.x, 2);
            bool pingPong = true;
            
            fftShader.SetInt(P_OPERATION, 1);
            fftShader.SetTexture(KERNEL_HORIZONTAL_STEP, P_BUFFER0, input);
            fftShader.SetTexture(KERNEL_HORIZONTAL_STEP, P_BUFFER1, buffer);
            for (int i = 0; i < logSize; i++)
            {
                pingPong = !pingPong;
                fftShader.SetInt(P_DEPTH, i);
                fftShader.SetBool(P_PING_PONG, pingPong);
                fftShader.Dispatch(KERNEL_HORIZONTAL_STEP, fftGroupSize.x, size.y, 1);
            }
            
            logSize = (int)Mathf.Log(size.y, 2);

            fftShader.SetTexture(KERNEL_VERTICAL_STEP, P_BUFFER0, input);
            fftShader.SetTexture(KERNEL_VERTICAL_STEP, P_BUFFER1, buffer);
            for (int i = 0; i < logSize; i++)
            {
                pingPong = !pingPong;
                fftShader.SetInt(P_DEPTH, i);
                fftShader.SetBool(P_PING_PONG, pingPong);
                fftShader.Dispatch(KERNEL_VERTICAL_STEP, size.x, fftGroupSize.y, 1);
            }

            if (pingPong) Graphics.Blit(buffer, input);

            fftShader.SetTexture(KERNEL_SHIFT, P_BUFFER0, input);
            fftShader.Dispatch(KERNEL_SHIFT, groupSize.x, groupSize.y, 1);
        }

        public void FFT2D(RenderTexture input, CommandBuffer cmd)
        {
            Vector2Int fftGroupSize = new Vector2Int(Mathf.Max(1, size.x >> 4), Mathf.Max(1, size.y >> 4));
            Vector2Int groupSize = new Vector2Int(Mathf.Max(1, size.x >> 3), Mathf.Max(1, size.y >> 3));
            cmd.SetComputeTextureParam(fftShader, KERNEL_BIT_REVERSE, P_BUFFER0, input);
            cmd.SetComputeTextureParam(fftShader, KERNEL_BIT_REVERSE, P_BUFFER1, buffer);
            cmd.DispatchCompute(fftShader, KERNEL_BIT_REVERSE, groupSize.x, groupSize.y, 1);

            int logSize = (int)Mathf.Log(size.x, 2);
            bool pingPong = true;
            
            cmd.SetComputeIntParam(fftShader, P_OPERATION, 1);
            cmd.SetComputeTextureParam(fftShader, KERNEL_HORIZONTAL_STEP, P_BUFFER0, input);
            cmd.SetComputeTextureParam(fftShader, KERNEL_HORIZONTAL_STEP, P_BUFFER1, buffer);
            for (int i = 0; i < logSize; i++)
            {
                pingPong = !pingPong;
                cmd.SetComputeIntParam(fftShader, P_DEPTH, i);
                cmd.SetComputeIntParam(fftShader, P_PING_PONG, pingPong ? 1 : 0);
                cmd.DispatchCompute(fftShader, KERNEL_HORIZONTAL_STEP, fftGroupSize.x, size.y, 1);
            }
            
            logSize = (int)Mathf.Log(size.y, 2);
            
            cmd.SetComputeTextureParam(fftShader, KERNEL_VERTICAL_STEP, P_BUFFER0, input);
            cmd.SetComputeTextureParam(fftShader, KERNEL_VERTICAL_STEP, P_BUFFER1, buffer);
            for (int i = 0; i < logSize; i++)
            {
                pingPong = !pingPong;
                cmd.SetComputeIntParam(fftShader, P_DEPTH, i);
                cmd.SetComputeIntParam(fftShader, P_PING_PONG, pingPong ? 1 : 0);
                cmd.DispatchCompute(fftShader, KERNEL_VERTICAL_STEP, size.x, groupSize.y, 1);
            }

            if (pingPong) cmd.Blit(buffer, input);

            cmd.SetComputeTextureParam(fftShader, KERNEL_SHIFT, P_BUFFER0, input);
            cmd.DispatchCompute(fftShader, KERNEL_SHIFT, groupSize.x, groupSize.y, 1);
        }
        
        public void IFFT2D(RenderTexture input, bool noScale = false)
        {
            Vector2Int fftGroupSize = new Vector2Int(Mathf.Max(1, size.x >> 4), Mathf.Max(1, size.y >> 4));
            Vector2Int groupSize = new Vector2Int(Mathf.Max(1, size.x >> 3), Mathf.Max(1, size.y >> 3));
            fftShader.SetTexture(KERNEL_BIT_REVERSE, P_BUFFER0, input);
            fftShader.SetTexture(KERNEL_BIT_REVERSE, P_BUFFER1, buffer);
            fftShader.Dispatch(KERNEL_BIT_REVERSE, groupSize.x, groupSize.y, 1);

            int logSize = (int)Mathf.Log(size.x, 2);
            bool pingPong = true;
            
            fftShader.SetInt(P_OPERATION, -1);
            fftShader.SetTexture(KERNEL_HORIZONTAL_STEP, P_BUFFER0, input);
            fftShader.SetTexture(KERNEL_HORIZONTAL_STEP, P_BUFFER1, buffer);
            for (int i = 0; i < logSize; i++)
            {
                pingPong = !pingPong;
                fftShader.SetInt(P_DEPTH, i);
                fftShader.SetBool(P_PING_PONG, pingPong);
                fftShader.Dispatch(KERNEL_HORIZONTAL_STEP, fftGroupSize.x, size.y, 1);
            }
            
            logSize = (int)Mathf.Log(size.y, 2);

            fftShader.SetTexture(KERNEL_VERTICAL_STEP, P_BUFFER0, input);
            fftShader.SetTexture(KERNEL_VERTICAL_STEP, P_BUFFER1, buffer);
            for (int i = 0; i < logSize; i++)
            {
                pingPong = !pingPong;
                fftShader.SetInt(P_DEPTH, i);
                fftShader.SetBool(P_PING_PONG, pingPong);
                fftShader.Dispatch(KERNEL_VERTICAL_STEP, size.x, fftGroupSize.y, 1);
            }

            if (pingPong) Graphics.Blit(buffer, input);

            fftShader.SetTexture(KERNEL_SHIFT, P_BUFFER0, input);
            fftShader.Dispatch(KERNEL_SHIFT, groupSize.x, groupSize.y, 1);
            
            if (noScale) return;
            fftShader.SetInts(P_SIZE, size.x, size.y);
            fftShader.SetTexture(KERNEL_SCALE, P_BUFFER0, input);
            fftShader.Dispatch(KERNEL_SCALE, groupSize.x, groupSize.y, 1);
        }

        public void IFFT2D(RenderTexture input, CommandBuffer cmd, bool noScale = false)
        {
            Vector2Int fftGroupSize = new Vector2Int(Mathf.Max(1, size.x >> 4), Mathf.Max(1, size.y >> 4));
            Vector2Int groupSize = new Vector2Int(Mathf.Max(1, size.x >> 3), Mathf.Max(1, size.y >> 3));
            cmd.SetComputeTextureParam(fftShader, KERNEL_BIT_REVERSE, P_BUFFER0, input);
            cmd.SetComputeTextureParam(fftShader, KERNEL_BIT_REVERSE, P_BUFFER1, buffer);
            cmd.DispatchCompute(fftShader, KERNEL_BIT_REVERSE, groupSize.x, groupSize.y, 1);

            int logSize = (int)Mathf.Log(size.x, 2);
            bool pingPong = true;
            
            cmd.SetComputeIntParam(fftShader, P_OPERATION, -1);
            cmd.SetComputeTextureParam(fftShader, KERNEL_HORIZONTAL_STEP, P_BUFFER0, input);
            cmd.SetComputeTextureParam(fftShader, KERNEL_HORIZONTAL_STEP, P_BUFFER1, buffer);
            for (int i = 0; i < logSize; i++)
            {
                pingPong = !pingPong;
                cmd.SetComputeIntParam(fftShader, P_DEPTH, i);
                cmd.SetComputeIntParam(fftShader, P_PING_PONG, pingPong ? 1 : 0);
                cmd.DispatchCompute(fftShader, KERNEL_HORIZONTAL_STEP, fftGroupSize.x, size.y, 1);
            }
            
            logSize = (int)Mathf.Log(size.y, 2);
            
            cmd.SetComputeTextureParam(fftShader, KERNEL_VERTICAL_STEP, P_BUFFER0, input);
            cmd.SetComputeTextureParam(fftShader, KERNEL_VERTICAL_STEP, P_BUFFER1, buffer);
            for (int i = 0; i < logSize; i++)
            {
                pingPong = !pingPong;
                cmd.SetComputeIntParam(fftShader, P_DEPTH, i);
                cmd.SetComputeIntParam(fftShader, P_PING_PONG, pingPong ? 1 : 0);
                cmd.DispatchCompute(fftShader, KERNEL_VERTICAL_STEP, size.x, groupSize.y, 1);
            }

            if (pingPong) cmd.Blit(buffer, input);

            cmd.SetComputeTextureParam(fftShader, KERNEL_SHIFT, P_BUFFER0, input);
            cmd.DispatchCompute(fftShader, KERNEL_SHIFT, groupSize.x, groupSize.y, 1);

            if (noScale) return;
            cmd.SetComputeIntParams(fftShader, P_SIZE, size.x, size.y);
            cmd.SetComputeTextureParam(fftShader, KERNEL_SCALE, P_BUFFER0, input);
            cmd.DispatchCompute(fftShader, KERNEL_SCALE, groupSize.x, groupSize.y, 1);
        }
        
        public void Allocate(Vector2Int size)
        {
            this.size = size;
            buffer = new RenderTexture(size.x, size.y, 0,
                RenderTextureFormat.RGFloat, RenderTextureReadWrite.Linear)
            {
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                enableRandomWrite = true
            };
            buffer.Create();
            
            uint[] hBitReverseIndices = new uint[size.x];
            uint[] vBitReverseIndices = new uint[size.y];
            for (uint i = 0; i < size.x; i++)
            {
                hBitReverseIndices[i] = hBitReverseIndices[i >> 1] >> 1 | (i & 1) * ((uint)size.x >> 1);
            }
            for (uint i = 0; i < size.y; i++)
            {
                vBitReverseIndices[i] = vBitReverseIndices[i >> 1] >> 1 | (i & 1) * ((uint)size.y >> 1);
            }
            this.hBitReverseIndices = new ComputeBuffer(size.x, sizeof(uint));
            this.vBitReverseIndices = new ComputeBuffer(size.y, sizeof(uint));
            this.hBitReverseIndices.SetData(hBitReverseIndices);
            this.vBitReverseIndices.SetData(vBitReverseIndices);
            fftShader.SetBuffer(KERNEL_BIT_REVERSE, P_HBIT_REV_INDICES, this.hBitReverseIndices);
            fftShader.SetBuffer(KERNEL_BIT_REVERSE, P_VBIT_REV_INDICES, this.vBitReverseIndices);
        }
        
        public void Dispose()
        {
            size = Vector2Int.zero;
            vBitReverseIndices?.Release();
            buffer?.Release();
        }

        private static readonly int P_BUFFER0 = Shader.PropertyToID("Buffer0");
        private static readonly int P_BUFFER1 = Shader.PropertyToID("Buffer1");
        private static readonly int P_SIZE = Shader.PropertyToID("Size");
        private static readonly int P_DEPTH = Shader.PropertyToID("Depth");
        private static readonly int P_OPERATION = Shader.PropertyToID("Operation");
        private static readonly int P_PING_PONG = Shader.PropertyToID("PingPong");
        private static readonly int P_HBIT_REV_INDICES = Shader.PropertyToID("HBitRevIndices");
        private static readonly int P_VBIT_REV_INDICES = Shader.PropertyToID("VBitRevIndices");
    }
