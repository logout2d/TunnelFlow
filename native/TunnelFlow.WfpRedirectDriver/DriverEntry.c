#include "SharedTypes.h"

TF_WFP_GLOBALS g_TfWfpGlobals = { 0 };

static
VOID
TfWfpDrainQueuedEvents(
    VOID
    )
{
    TF_WFP_REDIRECT_EVENT_V1 discarded = { 0 };

    while (TfWfpTryDequeueRedirectEvent(&discarded))
    {
    }
}

_Use_decl_annotations_
NTSTATUS
DriverEntry(
    PDRIVER_OBJECT DriverObject,
    PUNICODE_STRING RegistryPath
    )
{
    UNREFERENCED_PARAMETER(RegistryPath);

    NTSTATUS status;
    UNICODE_STRING deviceName;

    RtlInitUnicodeString(&deviceName, TF_WFP_DEVICE_NAME);
    RtlInitUnicodeString(&g_TfWfpGlobals.SymbolicLink, TF_WFP_SYMBOLIC_LINK);

    status = IoCreateDevice(
        DriverObject,
        0,
        &deviceName,
        FILE_DEVICE_NETWORK,
        FILE_DEVICE_SECURE_OPEN,
        FALSE,
        &g_TfWfpGlobals.DeviceObject);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    status = IoCreateSymbolicLink(&g_TfWfpGlobals.SymbolicLink, &deviceName);
    if (!NT_SUCCESS(status))
    {
        IoDeleteDevice(g_TfWfpGlobals.DeviceObject);
        g_TfWfpGlobals.DeviceObject = NULL;
        return status;
    }

    ExInitializeFastMutex(&g_TfWfpGlobals.ConfigLock);
    KeInitializeSpinLock(&g_TfWfpGlobals.QueueLock);
    InitializeListHead(&g_TfWfpGlobals.EventQueue);
    RtlZeroMemory(&g_TfWfpGlobals.Config, sizeof(g_TfWfpGlobals.Config));

    DriverObject->DriverUnload = TfWfpDriverUnload;
    DriverObject->MajorFunction[IRP_MJ_CREATE] = TfWfpCreateClose;
    DriverObject->MajorFunction[IRP_MJ_CLOSE] = TfWfpCreateClose;
    DriverObject->MajorFunction[IRP_MJ_DEVICE_CONTROL] = TfWfpDeviceControl;

    status = TfWfpRegisterCallout(g_TfWfpGlobals.DeviceObject);
    if (!NT_SUCCESS(status))
    {
        IoDeleteSymbolicLink(&g_TfWfpGlobals.SymbolicLink);
        IoDeleteDevice(g_TfWfpGlobals.DeviceObject);
        g_TfWfpGlobals.DeviceObject = NULL;
        return status;
    }

    return STATUS_SUCCESS;
}

_Use_decl_annotations_
VOID
TfWfpDriverUnload(
    PDRIVER_OBJECT DriverObject
    )
{
    UNREFERENCED_PARAMETER(DriverObject);

    TfWfpUnregisterCallout();
    TfWfpDrainQueuedEvents();

    if (g_TfWfpGlobals.DeviceObject != NULL)
    {
        IoDeleteSymbolicLink(&g_TfWfpGlobals.SymbolicLink);
        IoDeleteDevice(g_TfWfpGlobals.DeviceObject);
        g_TfWfpGlobals.DeviceObject = NULL;
    }
}

_Use_decl_annotations_
NTSTATUS
TfWfpCreateClose(
    PDEVICE_OBJECT DeviceObject,
    PIRP Irp
    )
{
    UNREFERENCED_PARAMETER(DeviceObject);

    Irp->IoStatus.Status = STATUS_SUCCESS;
    Irp->IoStatus.Information = 0;
    IoCompleteRequest(Irp, IO_NO_INCREMENT);
    return STATUS_SUCCESS;
}
