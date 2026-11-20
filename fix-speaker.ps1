# Switch the default playback device back to a real speaker (2026-11-16).
# Why: Lenovo LegionZone set its virtual speaker as the default endpoint, so the
# pet's voice went to a device nobody can hear.
# Usage:
#   powershell -ExecutionPolicy Bypass -File fix-speaker.ps1 -List
#   powershell -ExecutionPolicy Bypass -File fix-speaker.ps1 -Match Realtek
# NOTE: this file is intentionally pure ASCII. PowerShell 5.1 reads a BOM-less
# UTF-8 file as ANSI, and garbled quotes break the parser.

param([switch]$List, [switch]$Which, [string]$Match = "Realtek")

$source = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

[ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class PolicyConfigClient { }

[Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr format);
    int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int def, out IntPtr format);
    int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
    int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr end, IntPtr mix);
    int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int def, out long a, out long b);
    int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, long period);
    int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr mode);
    int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
    int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, out IntPtr value);
    int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(int flow, int state, out IntPtr devices);
    int GetDefaultAudioEndpoint(int flow, int role, out IntPtr device);
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr device);
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject { }

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    int GetCount(out int count);
    int Item(int index, out IntPtr device);
}

[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object i);
    int OpenPropertyStore(int access, out IPropertyStore store);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetState(out int state);
}

[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    int GetCount(out int count);
    int GetAt(int index, out PROPERTYKEY key);
    int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
    int Commit();
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY { public Guid fmtid; public int pid; }

[StructLayout(LayoutKind.Explicit)]
internal struct PROPVARIANT
{
    [FieldOffset(0)] public short vt;
    [FieldOffset(8)] public IntPtr pointerValue;
}

public static class AudioSwitch
{
    private static readonly Guid NameFmt = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");

    public static string[] List()
    {
        var list = new List<string>();
        var enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
        IntPtr collection;
        if (enumerator.EnumAudioEndpoints(0, 1, out collection) != 0) return list.ToArray();
        var devices = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(collection);
        int count;
        devices.GetCount(out count);
        for (int i = 0; i < count; i++)
        {
            IntPtr raw;
            if (devices.Item(i, out raw) != 0) continue;
            var device = (IMMDevice)Marshal.GetObjectForIUnknown(raw);
            string id;
            device.GetId(out id);
            list.Add(Name(device) + "\t" + id);
            Marshal.ReleaseComObject(device);
        }
        Marshal.ReleaseComObject(devices);
        return list.ToArray();
    }

    private static string Name(IMMDevice device)
    {
        try
        {
            IPropertyStore store;
            device.OpenPropertyStore(0, out store);
            var key = new PROPERTYKEY { fmtid = NameFmt, pid = 2 };
            PROPVARIANT value;
            store.GetValue(ref key, out value);
            return Marshal.PtrToStringUni(value.pointerValue) ?? "";
        }
        catch { return ""; }
    }

    // Which endpoint is the current default (console role)?
    public static string GetDefault()
    {
        var enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
        IntPtr raw;
        if (enumerator.GetDefaultAudioEndpoint(0, 0, out raw) != 0) return "none";
        var device = (IMMDevice)Marshal.GetObjectForIUnknown(raw);
        string id;
        device.GetId(out id);
        return id;
    }
    public static string SetDefaultById(string id)
    {
        var policy = (IPolicyConfig)(new PolicyConfigClient());
        int a = policy.SetDefaultEndpoint(id, 0);
        int b = policy.SetDefaultEndpoint(id, 1);
        int c = policy.SetDefaultEndpoint(id, 2);
        return "SetDefaultEndpoint returned " + a + "/" + b + "/" + c;
    }

    public static string SetDefault(string needle)
    {
        var enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
        IntPtr collection;
        if (enumerator.EnumAudioEndpoints(0, 1, out collection) != 0) return "enum failed";
        var devices = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(collection);
        int count;
        devices.GetCount(out count);
        string target = null, targetName = null;
        for (int i = 0; i < count; i++)
        {
            IntPtr raw;
            if (devices.Item(i, out raw) != 0) continue;
            var device = (IMMDevice)Marshal.GetObjectForIUnknown(raw);
            string id;
            device.GetId(out id);
            string name = Name(device);
            if (target == null && name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                target = id; targetName = name;
            }
            Marshal.ReleaseComObject(device);
        }
        Marshal.ReleaseComObject(devices);
        if (target == null) return "no device matching: " + needle;

        var policy = (IPolicyConfig)(new PolicyConfigClient());
        int a = policy.SetDefaultEndpoint(target, 0);
        int b = policy.SetDefaultEndpoint(target, 1);
        int c = policy.SetDefaultEndpoint(target, 2);
        return "default set to: " + targetName + "  (ret " + a + "/" + b + "/" + c + ")";
    }
}
'@

Add-Type -TypeDefinition $source -Language CSharp -ErrorAction Stop


# Endpoint id resolvable from the registry (name -> {0.0.0.00000000}.{guid}).
# COM property-store reading of names returns empty on some systems, so the id is
# looked up in the registry instead, where the key name IS the endpoint guid.
function Get-EndpointId([string]$needle) {
    $root = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render"
    foreach ($dev in Get-ChildItem $root -ErrorAction SilentlyContinue) {
        $state = (Get-ItemProperty $dev.PSPath -ErrorAction SilentlyContinue).DeviceState
        if ($state -ne 1) { continue }
        $props = Get-ItemProperty "$($dev.PSPath)\Properties" -ErrorAction SilentlyContinue
        $name = [string]$props."{a45c254e-df1c-4efd-8020-67d146a850e0},2"
        $desc = [string]$props."{b3f8fa53-0004-438e-9003-51a46e139bfc},6"
        if (($name + " " + $desc) -match $needle) {
            return "{0.0.0.00000000}." + $dev.PSChildName
        }
    }
    return $null
}
if ($Which) {
    Write-Output ("default now: " + [AudioSwitch]::GetDefault())
    exit 0
}
if ($List) {
    Write-Output "active playback devices:"
    foreach ($line in [AudioSwitch]::List()) {
        Write-Output ("  " + ($line -replace '\t', '  |  id='))
    }
    exit 0
}

$id = Get-EndpointId $Match
if ($null -eq $id) { Write-Output ("no device matching: " + $Match); exit 1 }
Write-Output ("endpoint: " + $id)
Write-Output ([AudioSwitch]::SetDefaultById($id))
