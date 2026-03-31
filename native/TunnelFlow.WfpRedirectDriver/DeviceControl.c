#include "SharedTypes.h"

BOOLEAN
TfWfpSnapshotConfig(
    _Out_ TF_WFP_DRIVER_CONFIG* Config
    )
{
    if (Config == NULL)
    {
        return FALSE;
    }

    ExAcquireFastMutex(&g_TfWfpGlobals.ConfigLock);
    *Config = g_TfWfpGlobals.Config;
    ExReleaseFastMutex(&g_TfWfpGlobals.ConfigLock);

    return TRUE;
}

NTSTATUS
TfWfpApplyConfigureRequest(
    _In_ const TF_WFP_CONFIGURE_REQUEST_V1* Request
    )
{
    if (Request == NULL)
    {
        return STATUS_INVALID_PARAMETER;
    }

    if (Request->Version != TF_WFP_CONTRACT_VERSION ||
        Request->Size < sizeof(TF_WFP_CONFIGURE_REQUEST_V1))
    {
        return STATUS_INVALID_PARAMETER;
    }

    ExAcquireFastMutex(&g_TfWfpGlobals.ConfigLock);

    RtlZeroMemory(&g_TfWfpGlobals.Config, sizeof(g_TfWfpGlobals.Config));
    g_TfWfpGlobals.Config.Enabled = TRUE;
    g_TfWfpGlobals.Config.DetailedLoggingEnabled =
        (Request->Flags & TF_WFP_CONFIG_FLAG_DETAILED_LOGGING) != 0;
    g_TfWfpGlobals.Config.RelayAddressV4 = Request->RelayAddressV4;
    g_TfWfpGlobals.Config.RelayPort = Request->RelayPort;

    RtlStringCchCopyW(
        g_TfWfpGlobals.Config.TestProcessPath,
        TF_WFP_MAX_PATH_CHARS,
        Request->TestProcessPath);

    ExReleaseFastMutex(&g_TfWfpGlobals.ConfigLock);

    return STATUS_SUCCESS;
}

VOID
TfWfpQueueRedirectEvent(
    _In_ const TF_WFP_REDIRECT_EVENT_V1* EventRecord
    )
{
    KIRQL oldIrql;
    TF_WFP_QUEUED_EVENT* queuedEvent;

    if (EventRecord == NULL)
    {
        return;
    }

    queuedEvent = (TF_WFP_QUEUED_EVENT*)ExAllocatePoolZero(
        NonPagedPoolNx,
        sizeof(TF_WFP_QUEUED_EVENT),
        'eRfT');
    if (queuedEvent == NULL)
    {
        return;
    }

    queuedEvent->EventRecord = *EventRecord;

    KeAcquireSpinLock(&g_TfWfpGlobals.QueueLock, &oldIrql);
    InsertTailList(&g_TfWfpGlobals.EventQueue, &queuedEvent->Link);
    KeReleaseSpinLock(&g_TfWfpGlobals.QueueLock, oldIrql);
}

BOOLEAN
TfWfpTryDequeueRedirectEvent(
    _Out_ TF_WFP_REDIRECT_EVENT_V1* EventRecord
    )
{
    BOOLEAN found = FALSE;
    KIRQL oldIrql;

    if (EventRecord == NULL)
    {
        return FALSE;
    }

    KeAcquireSpinLock(&g_TfWfpGlobals.QueueLock, &oldIrql);
    if (!IsListEmpty(&g_TfWfpGlobals.EventQueue))
    {
        PLIST_ENTRY link = RemoveHeadList(&g_TfWfpGlobals.EventQueue);
        TF_WFP_QUEUED_EVENT* queuedEvent =
            CONTAINING_RECORD(link, TF_WFP_QUEUED_EVENT, Link);

        *EventRecord = queuedEvent->EventRecord;
        ExFreePoolWithTag(queuedEvent, 'eRfT');
        found = TRUE;
    }
    KeReleaseSpinLock(&g_TfWfpGlobals.QueueLock, oldIrql);

    return found;
}

_Use_decl_annotations_
NTSTATUS
TfWfpDeviceControl(
    PDEVICE_OBJECT DeviceObject,
    PIRP Irp
    )
{
    UNREFERENCED_PARAMETER(DeviceObject);

    PIO_STACK_LOCATION stackLocation = IoGetCurrentIrpStackLocation(Irp);
    ULONG ioControlCode = stackLocation->Parameters.DeviceIoControl.IoControlCode;
    ULONG inputLength = stackLocation->Parameters.DeviceIoControl.InputBufferLength;
    ULONG outputLength = stackLocation->Parameters.DeviceIoControl.OutputBufferLength;
    PVOID systemBuffer = Irp->AssociatedIrp.SystemBuffer;
    NTSTATUS status = STATUS_INVALID_DEVICE_REQUEST;
    ULONG_PTR information = 0;

    switch (ioControlCode)
    {
    case TF_WFP_IOCTL_CONFIGURE:
        if (systemBuffer == NULL || inputLength < sizeof(TF_WFP_CONFIGURE_REQUEST_V1))
        {
            status = STATUS_BUFFER_TOO_SMALL;
            break;
        }

        status = TfWfpApplyConfigureRequest((const TF_WFP_CONFIGURE_REQUEST_V1*)systemBuffer);
        break;

    case TF_WFP_IOCTL_GET_NEXT_EVENT:
        if (systemBuffer == NULL || outputLength < sizeof(TF_WFP_REDIRECT_EVENT_V1))
        {
            status = STATUS_BUFFER_TOO_SMALL;
            break;
        }

        if (!TfWfpTryDequeueRedirectEvent((TF_WFP_REDIRECT_EVENT_V1*)systemBuffer))
        {
            status = STATUS_NO_MORE_ENTRIES;
            break;
        }

        information = sizeof(TF_WFP_REDIRECT_EVENT_V1);
        status = STATUS_SUCCESS;
        break;

    default:
        status = STATUS_INVALID_DEVICE_REQUEST;
        break;
    }

    Irp->IoStatus.Status = status;
    Irp->IoStatus.Information = information;
    IoCompleteRequest(Irp, IO_NO_INCREMENT);
    return status;
}
