#pragma once

#include <ntddk.h>
#include <fwpsk.h>
#include <fwpmk.h>
#include <ntstrsafe.h>

#define TF_WFP_MAX_PATH_CHARS 260

#define TF_WFP_DEVICE_NAME      L"\\Device\\TunnelFlowWfpRedirect"
#define TF_WFP_SYMBOLIC_LINK    L"\\DosDevices\\TunnelFlowWfpRedirect"
#define TF_WFP_DOS_DEVICE       L"\\\\.\\TunnelFlowWfpRedirect"

#define TF_WFP_IOCTL_CONFIGURE      CTL_CODE(0x8000, 0x800, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define TF_WFP_IOCTL_GET_NEXT_EVENT CTL_CODE(0x8000, 0x801, METHOD_BUFFERED, FILE_ANY_ACCESS)

#define TF_WFP_CONTRACT_VERSION 1
#define TF_WFP_CONFIG_FLAG_DETAILED_LOGGING 0x00000001UL

typedef struct _TF_WFP_CONFIGURE_REQUEST_V1
{
    UINT32 Version;
    UINT32 Size;
    UINT32 Flags;
    UINT32 RelayAddressV4;
    UINT16 RelayPort;
    UINT16 Reserved;
    WCHAR TestProcessPath[TF_WFP_MAX_PATH_CHARS];
} TF_WFP_CONFIGURE_REQUEST_V1;

typedef struct _TF_WFP_REDIRECT_EVENT_V1
{
    UINT32 Version;
    UINT32 Size;
    UINT32 LookupAddressV4;
    UINT32 OriginalAddressV4;
    UINT32 RelayAddressV4;
    UINT16 LookupPort;
    UINT16 OriginalPort;
    UINT16 RelayPort;
    UINT16 Reserved;
    UINT32 ProcessId;
    UINT32 Protocol;
    INT64 ObservedAtUtcTicks;
    GUID CorrelationId;
    WCHAR ProcessPath[TF_WFP_MAX_PATH_CHARS];
    WCHAR AppId[TF_WFP_MAX_PATH_CHARS];
} TF_WFP_REDIRECT_EVENT_V1;

typedef struct _TF_WFP_DRIVER_CONFIG
{
    BOOLEAN Enabled;
    BOOLEAN DetailedLoggingEnabled;
    UINT16 Reserved;
    UINT32 RelayAddressV4;
    UINT16 RelayPort;
    UINT16 ReservedPort;
    WCHAR TestProcessPath[TF_WFP_MAX_PATH_CHARS];
} TF_WFP_DRIVER_CONFIG;

typedef struct _TF_WFP_QUEUED_EVENT
{
    LIST_ENTRY Link;
    TF_WFP_REDIRECT_EVENT_V1 EventRecord;
} TF_WFP_QUEUED_EVENT;

typedef struct _TF_WFP_GLOBALS
{
    PDEVICE_OBJECT DeviceObject;
    UNICODE_STRING SymbolicLink;
    HANDLE EngineHandle;
    UINT32 RuntimeCalloutId;
    UINT64 FilterId;
    FAST_MUTEX ConfigLock;
    KSPIN_LOCK QueueLock;
    LIST_ENTRY EventQueue;
    TF_WFP_DRIVER_CONFIG Config;
} TF_WFP_GLOBALS;

extern TF_WFP_GLOBALS g_TfWfpGlobals;

DRIVER_INITIALIZE DriverEntry;
DRIVER_UNLOAD TfWfpDriverUnload;

DRIVER_DISPATCH TfWfpCreateClose;
DRIVER_DISPATCH TfWfpDeviceControl;

NTSTATUS
TfWfpRegisterCallout(
    _In_ PDEVICE_OBJECT DeviceObject
    );

VOID
TfWfpUnregisterCallout(
    VOID
    );

BOOLEAN
TfWfpSnapshotConfig(
    _Out_ TF_WFP_DRIVER_CONFIG* Config
    );

NTSTATUS
TfWfpApplyConfigureRequest(
    _In_ const TF_WFP_CONFIGURE_REQUEST_V1* Request
    );

VOID
TfWfpQueueRedirectEvent(
    _In_ const TF_WFP_REDIRECT_EVENT_V1* EventRecord
    );

BOOLEAN
TfWfpTryDequeueRedirectEvent(
    _Out_ TF_WFP_REDIRECT_EVENT_V1* EventRecord
    );
