using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Silk.NET.Core.Native;
using Silk.NET.Input;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using Buffer = Silk.NET.Vulkan.Buffer;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using Moose.Graphics.Hosting;
using Moose.Graphics.Primitives;

namespace Moose.Graphics.Rendering;

/// Vulkan 1.3 renderer: one pipeline drawing instanced SDF rounded rects and atlas glyphs with dynamic rendering.
internal sealed unsafe class VulkanRenderer : IDisposable
{
    const int FramesInFlight = 2;
    const int MaxQuads = 131072; // shortcut: fixed per-frame instance buffer (~9 MB per frame in flight) sized for a full-screen terminal grid on a 4K display; extra quads are dropped

    readonly IWindow window;
    readonly IInputContext input;
    readonly ILogger logger;
    readonly GlyphAtlas atlas;
    readonly Vk vk = Vk.GetApi();

    Instance instance;
    KhrSurface khrSurface = null!;
    SurfaceKHR surface;
    PhysicalDevice gpu;
    uint queueFamily;
    Device device;
    Queue queue;

    KhrSwapchain khrSwapchain = null!;
    SwapchainKHR swapchain;
    SurfaceFormatKHR format;
    Extent2D extent;
    Image[] images = [];
    ImageView[] views = [];
    Semaphore[] renderDone = []; // per swapchain image: the present engine may still hold it when the frame slot comes around

    Image atlasImage;
    DeviceMemory atlasMemory;
    ImageView atlasView;
    Sampler atlasSampler;
    Buffer atlasStaging;
    DeviceMemory atlasStagingMemory;
    nint atlasStagingMapped;
    DescriptorSetLayout descriptorLayout;
    DescriptorPool descriptorPool;
    DescriptorSet descriptorSet;

    PipelineLayout layout;
    Pipeline pipeline;
    CommandPool pool;
    readonly CommandBuffer[] cmds = new CommandBuffer[FramesInFlight];
    readonly Semaphore[] imageReady = new Semaphore[FramesInFlight];
    readonly Fence[] inFlight = new Fence[FramesInFlight];
    readonly Buffer[] quadBuffers = new Buffer[FramesInFlight];
    readonly DeviceMemory[] quadMemory = new DeviceMemory[FramesInFlight];
    readonly nint[] quadMapped = new nint[FramesInFlight];

    int frameIndex;
    bool resized;
    bool presented;

    // frames are drawn on demand: input, window events and Invalidate() mark the window dirty, otherwise the loop sleeps in WaitEvents
    readonly Lock wakeLock = new();
    readonly Timer wakeTimer;
    long wakeDue = long.MaxValue;
    int invalidated = 1;
    volatile bool closed;

    public IMouse Mouse { get; }
    public IKeyboard Keyboard { get; }
    public IWindow Window => window;
    /// Framebuffer px per logical px, exactly the mapping the viewport applies.
    public Vector2 Scale => new((float)extent.Width / window.Size.X, (float)extent.Height / window.Size.Y);

    public VulkanRenderer(WindowOptions options, GlyphAtlas atlas, ILogger logger)
    {
        this.logger = logger;
        this.atlas = atlas;
        window = Silk.NET.Windowing.Window.Create(options);
        window.Initialize();
        if (window.VkSurface is null) throw new PlatformNotSupportedException("Windowing platform doesn't support Vulkan.");
        input = window.CreateInput();
        Mouse = input.Mice[0];
        Keyboard = input.Keyboards[0];
        window.FramebufferResize += _ => { resized = true; Invalidate(); };
        window.Resize += _ => Invalidate();
        window.StateChanged += _ => Invalidate();
        window.FocusChanged += _ => Invalidate();
        Keyboard.KeyDown += (_, _, _) => Invalidate();
        Keyboard.KeyUp += (_, _, _) => Invalidate();
        Keyboard.KeyChar += (_, _) => Invalidate();
        Mouse.MouseDown += (_, _) => Invalidate();
        Mouse.MouseUp += (_, _) => Invalidate();
        Mouse.MouseMove += (_, _) => Invalidate();
        Mouse.Scroll += (_, _) => Invalidate();
        wakeTimer = new Timer(_ => Wake());

        CreateInstance();
        CreateDevice();
        CreateSwapchain();
        CreateAtlas();
        CreatePipeline();
        CreateFrames();
    }

    public void Close() => window.Close();

    /// Requests a frame; safe from any thread.
    public void Invalidate()
    {
        Interlocked.Exchange(ref invalidated, 1);
        if (!closed) window.ContinueEvents();
    }

    /// Requests a frame once delay has passed; the earliest pending request wins.
    public void Invalidate(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            Invalidate();
            return;
        }
        var due = Environment.TickCount64 + (long)Math.Ceiling(delay.TotalMilliseconds);
        lock (wakeLock)
        {
            if (due >= wakeDue) return;
            wakeDue = due;
            wakeTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    void Wake()
    {
        lock (wakeLock) wakeDue = long.MaxValue;
        Invalidate();
    }

    public void Run(Func<Vector2, Frame> buildFrame)
    {
        window.IsEventDriven = true;
        while (!window.IsClosing)
        {
            window.DoEvents();
            if (window.IsClosing) break;
            window.DoUpdate();
            if (Interlocked.Exchange(ref invalidated, 0) == 0) continue;
            if (window.FramebufferSize.X == 0 || window.FramebufferSize.Y == 0) continue; // minimized
            if (resized) RecreateSwapchain();
            var frame = buildFrame(new Vector2(window.Size.X, window.Size.Y));
            Draw(frame.Scene, frame.ClearColor);
        }
        closed = true;
        window.DoEvents();
        window.Reset();
        vk.DeviceWaitIdle(device);
    }

    void CreateInstance()
    {
        var extensions = window.VkSurface!.GetRequiredExtensions(out var extensionCount);
        // shortcut: no debug messenger, the validation layer prints to stdout by itself; add VK_EXT_debug_utils to route into ILogger
        string[] layers = IsDebug && HasLayer("VK_LAYER_KHRONOS_validation") ? ["VK_LAYER_KHRONOS_validation"] : [];
        var layerNames = SilkMarshal.StringArrayToPtr(layers);

        var appInfo = new ApplicationInfo { SType = StructureType.ApplicationInfo, ApiVersion = Vk.Version13 };
        var info = new InstanceCreateInfo
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo,
            EnabledExtensionCount = extensionCount,
            PpEnabledExtensionNames = extensions,
            EnabledLayerCount = (uint)layers.Length,
            PpEnabledLayerNames = (byte**)layerNames,
        };
        Check(vk.CreateInstance(in info, null, out instance), "vkCreateInstance");
        SilkMarshal.Free(layerNames);

        if (!vk.TryGetInstanceExtension(instance, out khrSurface)) throw new NotSupportedException("VK_KHR_surface is missing.");
        surface = window.VkSurface.Create<AllocationCallbacks>(instance.ToHandle(), null).ToSurface();
        logger.LogInformation("Vulkan instance created, validation layers: {Layers}", layers.Length > 0 ? "on" : "off");
    }

    bool HasLayer(string name)
    {
        uint count = 0;
        vk.EnumerateInstanceLayerProperties(ref count, null);
        var props = new LayerProperties[count];
        fixed (LayerProperties* p = props) vk.EnumerateInstanceLayerProperties(ref count, p);
        for (var i = 0; i < props.Length; i++)
        {
            var layer = props[i];
            if (SilkMarshal.PtrToString((nint)layer.LayerName) == name) return true;
        }
        return false;
    }

    void CreateDevice()
    {
        uint count = 0;
        vk.EnumeratePhysicalDevices(instance, ref count, null);
        var gpus = new PhysicalDevice[count];
        fixed (PhysicalDevice* p = gpus) vk.EnumeratePhysicalDevices(instance, ref count, p);

        foreach (var candidate in gpus.OrderBy(g => Properties(g).DeviceType != PhysicalDeviceType.DiscreteGpu))
        {
            if (Properties(candidate).ApiVersion < Vk.Version13 || FindQueueFamily(candidate) is not { } family) continue;
            (gpu, queueFamily) = (candidate, family);
            break;
        }
        if (gpu.Handle == 0) throw new NotSupportedException("No Vulkan 1.3 GPU with a graphics+present queue.");
        var gpuProps = Properties(gpu);
        logger.LogInformation("GPU: {Name}", SilkMarshal.PtrToString((nint)gpuProps.DeviceName));

        var priority = 1f;
        var queueInfo = new DeviceQueueCreateInfo { SType = StructureType.DeviceQueueCreateInfo, QueueFamilyIndex = queueFamily, QueueCount = 1, PQueuePriorities = &priority };
        var features13 = new PhysicalDeviceVulkan13Features { SType = StructureType.PhysicalDeviceVulkan13Features, DynamicRendering = true };
        var extensions = SilkMarshal.StringArrayToPtr([KhrSwapchain.ExtensionName]);
        var info = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            PNext = &features13,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueInfo,
            EnabledExtensionCount = 1,
            PpEnabledExtensionNames = (byte**)extensions,
        };
        Check(vk.CreateDevice(gpu, in info, null, out device), "vkCreateDevice");
        SilkMarshal.Free(extensions);

        vk.GetDeviceQueue(device, queueFamily, 0, out queue);
        if (!vk.TryGetDeviceExtension(instance, device, out khrSwapchain)) throw new NotSupportedException("VK_KHR_swapchain is missing.");
    }

    PhysicalDeviceProperties Properties(PhysicalDevice physicalDevice)
    {
        vk.GetPhysicalDeviceProperties(physicalDevice, out var props);
        return props;
    }

    // shortcut: requires one queue family doing both graphics and present (true on desktop GPUs); split them for exotic hardware
    uint? FindQueueFamily(PhysicalDevice physicalDevice)
    {
        uint count = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref count, null);
        var families = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* p = families) vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref count, p);
        for (uint i = 0; i < count; i++)
        {
            khrSurface.GetPhysicalDeviceSurfaceSupport(physicalDevice, i, surface, out var present);
            if (families[i].QueueFlags.HasFlag(QueueFlags.GraphicsBit) && present) return i;
        }
        return null;
    }

    void CreateSwapchain()
    {
        khrSurface.GetPhysicalDeviceSurfaceCapabilities(gpu, surface, out var caps);
        uint count = 0;
        khrSurface.GetPhysicalDeviceSurfaceFormats(gpu, surface, ref count, null);
        var formats = new SurfaceFormatKHR[count];
        fixed (SurfaceFormatKHR* p = formats) khrSurface.GetPhysicalDeviceSurfaceFormats(gpu, surface, ref count, p);
        // UNORM on purpose: colors are sRGB values and get blended in sRGB space, like browsers do
        format = formats.FirstOrDefault(f => f.Format == Format.B8G8R8A8Unorm, formats[0]);

        // Wayland reports currentExtent = 0xFFFFFFFF: the swapchain decides, so take the framebuffer size
        var fb = window.FramebufferSize;
        extent = caps.CurrentExtent.Width != uint.MaxValue ? caps.CurrentExtent : new Extent2D(
            Math.Clamp((uint)fb.X, caps.MinImageExtent.Width, caps.MaxImageExtent.Width),
            Math.Clamp((uint)fb.Y, caps.MinImageExtent.Height, caps.MaxImageExtent.Height));

        var info = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = surface,
            MinImageCount = caps.MaxImageCount == 0 ? caps.MinImageCount + 1 : Math.Min(caps.MinImageCount + 1, caps.MaxImageCount),
            ImageFormat = format.Format,
            ImageColorSpace = format.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit,
            ImageSharingMode = SharingMode.Exclusive,
            PreTransform = caps.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = PresentModeKHR.FifoKhr,
            Clipped = true,
        };
        Check(khrSwapchain.CreateSwapchain(device, in info, null, out swapchain), "vkCreateSwapchainKHR");

        khrSwapchain.GetSwapchainImages(device, swapchain, ref count, null);
        images = new Image[count];
        fixed (Image* p = images) khrSwapchain.GetSwapchainImages(device, swapchain, ref count, p);

        views = new ImageView[count];
        renderDone = new Semaphore[count];
        for (var i = 0; i < count; i++)
        {
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = images[i],
                ViewType = ImageViewType.Type2D,
                Format = format.Format,
                SubresourceRange = ColorRange,
            };
            Check(vk.CreateImageView(device, in viewInfo, null, out views[i]), "vkCreateImageView");
            renderDone[i] = CreateSemaphore();
        }
    }

    void DestroySwapchain()
    {
        foreach (var view in views) vk.DestroyImageView(device, view, null);
        foreach (var semaphore in renderDone) vk.DestroySemaphore(device, semaphore, null);
        khrSwapchain.DestroySwapchain(device, swapchain, null);
    }

    void RecreateSwapchain()
    {
        vk.DeviceWaitIdle(device);
        DestroySwapchain();
        CreateSwapchain();
        resized = false;
    }

    void CreatePipeline()
    {
        var vert = CreateShader("quad.vert.spv");
        var frag = CreateShader("quad.frag.spv");
        var entry = (byte*)SilkMarshal.StringToPtr("main");

        var stages = stackalloc PipelineShaderStageCreateInfo[2];
        stages[0] = new() { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit, Module = vert, PName = entry };
        stages[1] = new() { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit, Module = frag, PName = entry };

        var binding = new VertexInputBindingDescription { Binding = 0, Stride = (uint)sizeof(Quad), InputRate = VertexInputRate.Instance };
        var attributes = stackalloc VertexInputAttributeDescription[5];
        attributes[0] = new() { Location = 0, Format = Format.R32G32B32A32Sfloat, Offset = 0 };  // rect
        attributes[1] = new() { Location = 1, Format = Format.R32G32B32A32Sfloat, Offset = 16 }; // color
        attributes[2] = new() { Location = 2, Format = Format.R32G32B32A32Sfloat, Offset = 32 }; // border color
        attributes[3] = new() { Location = 3, Format = Format.R32G32Sfloat, Offset = 48 };       // radius, border width
        attributes[4] = new() { Location = 4, Format = Format.R32G32B32A32Sfloat, Offset = 56 }; // glyph uv

        var vertexInput = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 1,
            PVertexBindingDescriptions = &binding,
            VertexAttributeDescriptionCount = 5,
            PVertexAttributeDescriptions = attributes,
        };
        var assembly = new PipelineInputAssemblyStateCreateInfo { SType = StructureType.PipelineInputAssemblyStateCreateInfo, Topology = PrimitiveTopology.TriangleStrip };
        var viewport = new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, ScissorCount = 1 };
        var raster = new PipelineRasterizationStateCreateInfo { SType = StructureType.PipelineRasterizationStateCreateInfo, PolygonMode = PolygonMode.Fill, CullMode = CullModeFlags.None, LineWidth = 1 };
        var multisample = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = SampleCountFlags.Count1Bit };
        // premultiplied alpha: the fragment shader outputs color * coverage
        var blendAttachment = new PipelineColorBlendAttachmentState
        {
            BlendEnable = true,
            SrcColorBlendFactor = BlendFactor.One,
            DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
            ColorBlendOp = BlendOp.Add,
            SrcAlphaBlendFactor = BlendFactor.One,
            DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
            AlphaBlendOp = BlendOp.Add,
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
        };
        var blend = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = 1, PAttachments = &blendAttachment };
        var dynamicStates = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
        var dynamic = new PipelineDynamicStateCreateInfo { SType = StructureType.PipelineDynamicStateCreateInfo, DynamicStateCount = 2, PDynamicStates = dynamicStates };

        var push = new PushConstantRange { StageFlags = ShaderStageFlags.VertexBit, Size = (uint)sizeof(Vector2) };
        var setLayout = descriptorLayout;
        var layoutInfo = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &setLayout, PushConstantRangeCount = 1, PPushConstantRanges = &push };
        Check(vk.CreatePipelineLayout(device, in layoutInfo, null, out layout), "vkCreatePipelineLayout");

        // shortcut: pipeline is baked for the first swapchain format; recreate it too if a later swapchain picks another format
        var colorFormat = format.Format;
        var rendering = new PipelineRenderingCreateInfo { SType = StructureType.PipelineRenderingCreateInfo, ColorAttachmentCount = 1, PColorAttachmentFormats = &colorFormat };
        var info = new GraphicsPipelineCreateInfo
        {
            SType = StructureType.GraphicsPipelineCreateInfo,
            PNext = &rendering,
            StageCount = 2,
            PStages = stages,
            PVertexInputState = &vertexInput,
            PInputAssemblyState = &assembly,
            PViewportState = &viewport,
            PRasterizationState = &raster,
            PMultisampleState = &multisample,
            PColorBlendState = &blend,
            PDynamicState = &dynamic,
            Layout = layout,
        };
        Check(vk.CreateGraphicsPipelines(device, default, 1, in info, null, out pipeline), "vkCreateGraphicsPipelines");

        vk.DestroyShaderModule(device, vert, null);
        vk.DestroyShaderModule(device, frag, null);
        SilkMarshal.Free((nint)entry);
    }

    // R8 image + sampler bound once as set 0; pixels arrive through a persistently mapped staging buffer in UploadAtlas
    void CreateAtlas()
    {
        var size = (uint)atlas.Size;
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8Unorm,
            Extent = new Extent3D(size, size, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Check(vk.CreateImage(device, in imageInfo, null, out atlasImage), "vkCreateImage");
        vk.GetImageMemoryRequirements(device, atlasImage, out var imageReq);
        atlasMemory = Allocate(imageReq, MemoryPropertyFlags.DeviceLocalBit);
        Check(vk.BindImageMemory(device, atlasImage, atlasMemory, 0), "vkBindImageMemory");

        var viewInfo = new ImageViewCreateInfo { SType = StructureType.ImageViewCreateInfo, Image = atlasImage, ViewType = ImageViewType.Type2D, Format = Format.R8Unorm, SubresourceRange = ColorRange };
        Check(vk.CreateImageView(device, in viewInfo, null, out atlasView), "vkCreateImageView");
        // glyph quads land 1:1 on framebuffer pixels (TextSystem snaps them), so the filter only absorbs float rounding
        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
        };
        Check(vk.CreateSampler(device, in samplerInfo, null, out atlasSampler), "vkCreateSampler");

        var stagingInfo = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = size * size, Usage = BufferUsageFlags.TransferSrcBit, SharingMode = SharingMode.Exclusive };
        Check(vk.CreateBuffer(device, in stagingInfo, null, out atlasStaging), "vkCreateBuffer");
        vk.GetBufferMemoryRequirements(device, atlasStaging, out var stagingReq);
        atlasStagingMemory = Allocate(stagingReq, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        Check(vk.BindBufferMemory(device, atlasStaging, atlasStagingMemory, 0), "vkBindBufferMemory");
        void* mapped;
        Check(vk.MapMemory(device, atlasStagingMemory, 0, stagingInfo.Size, 0, &mapped), "vkMapMemory");
        atlasStagingMapped = (nint)mapped;

        var binding = new DescriptorSetLayoutBinding { Binding = 0, DescriptorType = DescriptorType.CombinedImageSampler, DescriptorCount = 1, StageFlags = ShaderStageFlags.FragmentBit };
        var setLayoutInfo = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 1, PBindings = &binding };
        Check(vk.CreateDescriptorSetLayout(device, in setLayoutInfo, null, out descriptorLayout), "vkCreateDescriptorSetLayout");
        var poolSize = new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = 1 };
        var poolInfo = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = 1, PoolSizeCount = 1, PPoolSizes = &poolSize };
        Check(vk.CreateDescriptorPool(device, in poolInfo, null, out descriptorPool), "vkCreateDescriptorPool");
        var setLayout = descriptorLayout;
        var allocInfo = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = descriptorPool, DescriptorSetCount = 1, PSetLayouts = &setLayout };
        Check(vk.AllocateDescriptorSets(device, in allocInfo, out descriptorSet), "vkAllocateDescriptorSets");

        var imageDescriptor = new DescriptorImageInfo { Sampler = atlasSampler, ImageView = atlasView, ImageLayout = ImageLayout.ShaderReadOnlyOptimal };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = descriptorSet,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageDescriptor,
        };
        vk.UpdateDescriptorSets(device, 1, in write, 0, null);
    }

    // shortcut: re-uploads the whole atlas and idles the GPU when any glyph was added; fine while new glyphs are rare, upload dirty rects into a per-frame staging slot if typing causes hitches
    void UploadAtlas(CommandBuffer cmd)
    {
        vk.DeviceWaitIdle(device); // the other frame in flight may still sample the image or read the staging buffer
        atlas.Pixels.CopyTo(new Span<byte>((void*)atlasStagingMapped, atlas.Pixels.Length));
        atlas.Dirty = false;

        AtlasBarrier(cmd, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, 0, AccessFlags.TransferWriteBit, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit);
        var size = (uint)atlas.Size;
        var region = new BufferImageCopy
        {
            ImageSubresource = new ImageSubresourceLayers { AspectMask = ImageAspectFlags.ColorBit, LayerCount = 1 },
            ImageExtent = new Extent3D(size, size, 1),
        };
        vk.CmdCopyBufferToImage(cmd, atlasStaging, atlasImage, ImageLayout.TransferDstOptimal, 1, in region);
        AtlasBarrier(cmd, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit);
    }

    void AtlasBarrier(CommandBuffer cmd, ImageLayout from, ImageLayout to, AccessFlags srcAccess, AccessFlags dstAccess, PipelineStageFlags srcStage, PipelineStageFlags dstStage)
    {
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = from,
            NewLayout = to,
            SrcAccessMask = srcAccess,
            DstAccessMask = dstAccess,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = atlasImage,
            SubresourceRange = ColorRange,
        };
        vk.CmdPipelineBarrier(cmd, srcStage, dstStage, 0, 0, null, 0, null, 1, &barrier);
    }

    DeviceMemory Allocate(MemoryRequirements requirements, MemoryPropertyFlags flags)
    {
        var info = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, AllocationSize = requirements.Size, MemoryTypeIndex = FindMemoryType(requirements.MemoryTypeBits, flags) };
        Check(vk.AllocateMemory(device, in info, null, out var memory), "vkAllocateMemory");
        return memory;
    }

    ShaderModule CreateShader(string name)
    {
        using var stream = typeof(VulkanRenderer).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded shader {name}.");
        var code = new byte[stream.Length];
        stream.ReadExactly(code);
        fixed (byte* p = code)
        {
            var info = new ShaderModuleCreateInfo { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)code.Length, PCode = (uint*)p };
            Check(vk.CreateShaderModule(device, in info, null, out var module), "vkCreateShaderModule");
            return module;
        }
    }

    void CreateFrames()
    {
        var poolInfo = new CommandPoolCreateInfo { SType = StructureType.CommandPoolCreateInfo, Flags = CommandPoolCreateFlags.ResetCommandBufferBit, QueueFamilyIndex = queueFamily };
        Check(vk.CreateCommandPool(device, in poolInfo, null, out pool), "vkCreateCommandPool");
        var alloc = new CommandBufferAllocateInfo { SType = StructureType.CommandBufferAllocateInfo, CommandPool = pool, Level = CommandBufferLevel.Primary, CommandBufferCount = FramesInFlight };
        fixed (CommandBuffer* p = cmds) Check(vk.AllocateCommandBuffers(device, in alloc, p), "vkAllocateCommandBuffers");

        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };
        var bufferInfo = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = (ulong)(MaxQuads * sizeof(Quad)), Usage = BufferUsageFlags.VertexBufferBit, SharingMode = SharingMode.Exclusive };
        for (var i = 0; i < FramesInFlight; i++)
        {
            imageReady[i] = CreateSemaphore();
            Check(vk.CreateFence(device, in fenceInfo, null, out inFlight[i]), "vkCreateFence");

            Check(vk.CreateBuffer(device, in bufferInfo, null, out quadBuffers[i]), "vkCreateBuffer");
            vk.GetBufferMemoryRequirements(device, quadBuffers[i], out var req);
            var memInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = req.Size,
                MemoryTypeIndex = FindMemoryType(req.MemoryTypeBits, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit),
            };
            Check(vk.AllocateMemory(device, in memInfo, null, out quadMemory[i]), "vkAllocateMemory");
            Check(vk.BindBufferMemory(device, quadBuffers[i], quadMemory[i], 0), "vkBindBufferMemory");
            void* mapped;
            Check(vk.MapMemory(device, quadMemory[i], 0, bufferInfo.Size, 0, &mapped), "vkMapMemory");
            quadMapped[i] = (nint)mapped;
        }
    }

    uint FindMemoryType(uint bits, MemoryPropertyFlags flags)
    {
        vk.GetPhysicalDeviceMemoryProperties(gpu, out var mem);
        for (var i = 0; i < mem.MemoryTypeCount; i++)
            if ((bits & (1u << i)) != 0 && (mem.MemoryTypes[i].PropertyFlags & flags) == flags) return (uint)i;
        throw new NotSupportedException($"No memory type with {flags}.");
    }

    Semaphore CreateSemaphore()
    {
        var info = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        Check(vk.CreateSemaphore(device, in info, null, out var semaphore), "vkCreateSemaphore");
        return semaphore;
    }

    void Draw(List<Quad> scene, Color clear)
    {
        var fence = inFlight[frameIndex];
        vk.WaitForFences(device, 1, in fence, true, ulong.MaxValue);

        uint imageIndex = 0;
        var result = khrSwapchain.AcquireNextImage(device, swapchain, ulong.MaxValue, imageReady[frameIndex], default, ref imageIndex);
        if (result == Result.ErrorOutOfDateKhr) { RecreateSwapchain(); return; }
        if (result != Result.SuboptimalKhr) Check(result, "vkAcquireNextImageKHR");
        vk.ResetFences(device, 1, in fence);

        var count = Math.Min(scene.Count, MaxQuads);
        CollectionsMarshal.AsSpan(scene)[..count].CopyTo(new Span<Quad>((void*)quadMapped[frameIndex], count));

        var cmd = cmds[frameIndex];
        var image = images[imageIndex];
        vk.ResetCommandBuffer(cmd, 0);
        var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
        Check(vk.BeginCommandBuffer(cmd, in begin), "vkBeginCommandBuffer");
        if (atlas.Dirty) UploadAtlas(cmd);

        Barrier(cmd, image, ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal, 0, AccessFlags.ColorAttachmentWriteBit, PipelineStageFlags.ColorAttachmentOutputBit);
        var attachment = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = views[imageIndex],
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            ClearValue = new ClearValue { Color = new ClearColorValue { Float32_0 = clear.R, Float32_1 = clear.G, Float32_2 = clear.B, Float32_3 = clear.A } },
        };
        var area = new Rect2D { Extent = extent };
        var rendering = new RenderingInfo { SType = StructureType.RenderingInfo, RenderArea = area, LayerCount = 1, ColorAttachmentCount = 1, PColorAttachments = &attachment };
        vk.CmdBeginRendering(cmd, in rendering);

        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, pipeline);
        var set = descriptorSet;
        vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, layout, 0, 1, in set, 0, null);
        var viewport = new Viewport { Width = extent.Width, Height = extent.Height, MaxDepth = 1 };
        vk.CmdSetViewport(cmd, 0, 1, in viewport);
        vk.CmdSetScissor(cmd, 0, 1, in area);
        // layout works in logical px, the viewport maps it onto the (maybe HiDPI) framebuffer
        var logicalSize = new Vector2(window.Size.X, window.Size.Y);
        vk.CmdPushConstants(cmd, layout, ShaderStageFlags.VertexBit, 0, (uint)sizeof(Vector2), &logicalSize);
        var buffer = quadBuffers[frameIndex];
        ulong offset = 0;
        vk.CmdBindVertexBuffers(cmd, 0, 1, in buffer, in offset);
        vk.CmdDraw(cmd, 4, (uint)count, 0, 0);

        vk.CmdEndRendering(cmd);
        Barrier(cmd, image, ImageLayout.ColorAttachmentOptimal, ImageLayout.PresentSrcKhr, AccessFlags.ColorAttachmentWriteBit, 0, PipelineStageFlags.BottomOfPipeBit);
        Check(vk.EndCommandBuffer(cmd), "vkEndCommandBuffer");

        var wait = imageReady[frameIndex];
        var signal = renderDone[imageIndex];
        var waitStage = PipelineStageFlags.ColorAttachmentOutputBit;
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &wait,
            PWaitDstStageMask = &waitStage,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &signal,
        };
        Check(vk.QueueSubmit(queue, 1, in submit, fence), "vkQueueSubmit");

        var chain = swapchain;
        var present = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &signal,
            SwapchainCount = 1,
            PSwapchains = &chain,
            PImageIndices = &imageIndex,
        };
        result = khrSwapchain.QueuePresent(queue, in present);
        if (!presented && result == Result.Success)
        {
            presented = true;
            logger.LogInformation("First frame presented: {Width}x{Height}, {Images} swapchain images, {Format}, {Quads} quads",
                extent.Width, extent.Height, images.Length, format.Format, count);
        }
        if (result is Result.ErrorOutOfDateKhr or Result.SuboptimalKhr || resized) RecreateSwapchain();
        else Check(result, "vkQueuePresentKHR");

        frameIndex = (frameIndex + 1) % FramesInFlight;
    }

    // srcStage doubles as dstStage on the way in: it chains with the acquire semaphore wait at ColorAttachmentOutput
    void Barrier(CommandBuffer cmd, Image image, ImageLayout from, ImageLayout to, AccessFlags srcAccess, AccessFlags dstAccess, PipelineStageFlags dstStage)
    {
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = from,
            NewLayout = to,
            SrcAccessMask = srcAccess,
            DstAccessMask = dstAccess,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = ColorRange,
        };
        vk.CmdPipelineBarrier(cmd, PipelineStageFlags.ColorAttachmentOutputBit, dstStage, 0, 0, null, 0, null, 1, &barrier);
    }

    static ImageSubresourceRange ColorRange => new() { AspectMask = ImageAspectFlags.ColorBit, LevelCount = 1, LayerCount = 1 };

    static void Check(Result result, string call)
    {
        if (result != Result.Success) throw new InvalidOperationException($"{call} failed: {result}");
    }

#if DEBUG
    const bool IsDebug = true;
#else
    const bool IsDebug = false;
#endif

    public void Dispose()
    {
        closed = true;
        wakeTimer.Dispose();
        vk.DeviceWaitIdle(device);
        for (var i = 0; i < FramesInFlight; i++)
        {
            vk.DestroyBuffer(device, quadBuffers[i], null);
            vk.FreeMemory(device, quadMemory[i], null);
            vk.DestroySemaphore(device, imageReady[i], null);
            vk.DestroyFence(device, inFlight[i], null);
        }
        vk.DestroyCommandPool(device, pool, null);
        vk.DestroyPipeline(device, pipeline, null);
        vk.DestroyPipelineLayout(device, layout, null);
        vk.DestroyDescriptorPool(device, descriptorPool, null);
        vk.DestroyDescriptorSetLayout(device, descriptorLayout, null);
        vk.DestroySampler(device, atlasSampler, null);
        vk.DestroyImageView(device, atlasView, null);
        vk.DestroyImage(device, atlasImage, null);
        vk.FreeMemory(device, atlasMemory, null);
        vk.DestroyBuffer(device, atlasStaging, null);
        vk.FreeMemory(device, atlasStagingMemory, null);
        DestroySwapchain();
        vk.DestroyDevice(device, null);
        khrSurface.DestroySurface(instance, surface, null);
        vk.DestroyInstance(instance, null);
        input.Dispose();
        window.Dispose();
        vk.Dispose();
    }
}
