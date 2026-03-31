#include "SharedTypes.h"

// Fixed GUIDs for the first event-only ALE slice.
static const GUID TF_WFP_SUBLAYER_GUID =
{ 0xb92d5d4e, 0x3058, 0x4ab9, { 0x92, 0x4c, 0x8d, 0x1d, 0x87, 0x56, 0x8b, 0x44 } };
static const GUID TF_WFP_CALLOUT_GUID =
{ 0x5a2c7935, 0x6620, 0x46eb, { 0xa8, 0xe8, 0xd3, 0x61, 0x27, 0x50, 0xb6, 0x51 } };

static
BOOLEAN
TfWfpPathMatchesConfig(
    _In_reads_bytes_opt_(BlobSize) const UINT8* BlobData,
    _In_ SIZE_T BlobSize,
    _In_ const TF_WFP_DRIVER_CONFIG* Config,
    _Out_writes_opt_(TF_WFP_MAX_PATH_CHARS) PWSTR CopiedPath
    )
{
    BOOLEAN matches = FALSE;
    SIZE_T wcharCount;

    if (CopiedPath != NULL)
    {
        CopiedPath[0] = L'\0';
    }

    if (BlobData == NULL || BlobSize < sizeof(WCHAR))
    {
        return FALSE;
    }

    wcharCount = BlobSize / sizeof(WCHAR);
    if (CopiedPath != NULL)
    {
        RtlStringCchCopyNW(CopiedPath, TF_WFP_MAX_PATH_CHARS, (PCWSTR)BlobData, wcharCount);
    }

    if (Config->TestProcessPath[0] == L'\0')
    {
        return TRUE;
    }

    {
        UNICODE_STRING configuredPath;
        UNICODE_STRING incomingPath;

        RtlInitUnicodeString(&configuredPath, Config->TestProcessPath);
        incomingPath.Buffer = (PWSTR)BlobData;
        incomingPath.Length = (USHORT)(BlobSize - sizeof(WCHAR));
        incomingPath.MaximumLength = (USHORT)BlobSize;

        matches = RtlEqualUnicodeString(&configuredPath, &incomingPath, TRUE);
    }

    return matches;
}

static
VOID
TfWfpPopulateEvent(
    _In_ const FWPS_INCOMING_VALUES0* InFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0* InMetaValues,
    _In_ const TF_WFP_DRIVER_CONFIG* Config,
    _Out_ TF_WFP_REDIRECT_EVENT_V1* EventRecord
    )
{
    FWP_VALUE0 localAddressValue;
    FWP_VALUE0 localPortValue;
    FWP_VALUE0 remoteAddressValue;
    FWP_VALUE0 remotePortValue;
    FWP_VALUE0 appIdValue;

    RtlZeroMemory(EventRecord, sizeof(*EventRecord));
    EventRecord->Version = TF_WFP_CONTRACT_VERSION;
    EventRecord->Size = sizeof(*EventRecord);
    EventRecord->LookupAddressV4 =
        InFixedValues->incomingValue[FWPS_FIELD_ALE_CONNECT_REDIRECT_V4_IP_LOCAL_ADDRESS].value.uint32;
    EventRecord->OriginalAddressV4 =
        InFixedValues->incomingValue[FWPS_FIELD_ALE_CONNECT_REDIRECT_V4_IP_REMOTE_ADDRESS].value.uint32;
    EventRecord->RelayAddressV4 = Config->RelayAddressV4;
    EventRecord->LookupPort =
        InFixedValues->incomingValue[FWPS_FIELD_ALE_CONNECT_REDIRECT_V4_IP_LOCAL_PORT].value.uint16;
    EventRecord->OriginalPort =
        InFixedValues->incomingValue[FWPS_FIELD_ALE_CONNECT_REDIRECT_V4_IP_REMOTE_PORT].value.uint16;
    EventRecord->RelayPort = Config->RelayPort;
    EventRecord->Protocol =
        InFixedValues->incomingValue[FWPS_FIELD_ALE_CONNECT_REDIRECT_V4_IP_PROTOCOL].value.uint8;

    if ((InMetaValues->currentMetadataValues & FWPS_METADATA_FIELD_PROCESS_ID) != 0)
    {
        EventRecord->ProcessId = (UINT32)(UINT_PTR)InMetaValues->processId;
    }

    localAddressValue = InFixedValues->incomingValue[FWPS_FIELD_ALE_CONNECT_REDIRECT_V4_IP_LOCAL_ADDRESS].value;
    localPortValue = InFixedValues->incomingValue[FWPS_FIELD_ALE_CONNECT_REDIRECT_V4_IP_LOCAL_PORT].value;
    remoteAddressValue = InFixedValues->incomingValue[FWPS_FIELD_ALE_CONNECT_REDIRECT_V4_IP_REMOTE_ADDRESS].value;
    remotePortValue = InFixedValues->incomingValue[FWPS_FIELD_ALE_CONNECT_REDIRECT_V4_IP_REMOTE_PORT].value;
    appIdValue = InFixedValues->incomingValue[FWPS_FIELD_ALE_CONNECT_REDIRECT_V4_ALE_APP_ID].value;

    UNREFERENCED_PARAMETER(localAddressValue);
    UNREFERENCED_PARAMETER(localPortValue);
    UNREFERENCED_PARAMETER(remoteAddressValue);
    UNREFERENCED_PARAMETER(remotePortValue);

    if (appIdValue.type == FWP_BYTE_BLOB_TYPE && appIdValue.byteBlob != NULL)
    {
        RtlStringCchCopyNW(
            EventRecord->ProcessPath,
            TF_WFP_MAX_PATH_CHARS,
            (PCWSTR)appIdValue.byteBlob->data,
            appIdValue.byteBlob->size / sizeof(WCHAR));

        RtlStringCchCopyW(
            EventRecord->AppId,
            TF_WFP_MAX_PATH_CHARS,
            EventRecord->ProcessPath);
    }

    ExUuidCreate(&EventRecord->CorrelationId);
}

static
VOID
NTAPI
TfWfpClassifyConnectRedirect(
    _In_ const FWPS_INCOMING_VALUES0* InFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0* InMetaValues,
    _Inout_opt_ VOID* LayerData,
    _In_opt_ const VOID* ClassifyContext,
    _In_ const FWPS_FILTER3* Filter,
    _In_ UINT64 FlowContext,
    _Inout_ FWPS_CLASSIFY_OUT0* ClassifyOut
    )
{
    TF_WFP_DRIVER_CONFIG config;
    TF_WFP_REDIRECT_EVENT_V1 eventRecord;
    const FWP_VALUE0* protocolValue;
    const FWP_VALUE0* appIdValue;

    UNREFERENCED_PARAMETER(LayerData);
    UNREFERENCED_PARAMETER(ClassifyContext);
    UNREFERENCED_PARAMETER(Filter);
    UNREFERENCED_PARAMETER(FlowContext);

    ClassifyOut->actionType = FWP_ACTION_PERMIT;

    if (!TfWfpSnapshotConfig(&config) || !config.Enabled)
    {
        return;
    }

    protocolValue = &InFixedValues->incomingValue[FWPS_FIELD_ALE_CONNECT_REDIRECT_V4_IP_PROTOCOL].value;
    if (protocolValue->uint8 != IPPROTO_TCP)
    {
        return;
    }

    appIdValue = &InFixedValues->incomingValue[FWPS_FIELD_ALE_CONNECT_REDIRECT_V4_ALE_APP_ID].value;
    if (appIdValue->type != FWP_BYTE_BLOB_TYPE ||
        appIdValue->byteBlob == NULL ||
        !TfWfpPathMatchesConfig(
            appIdValue->byteBlob->data,
            appIdValue->byteBlob->size,
            &config,
            NULL))
    {
        return;
    }

    TfWfpPopulateEvent(InFixedValues, InMetaValues, &config, &eventRecord);
    TfWfpQueueRedirectEvent(&eventRecord);
}

static
NTSTATUS
NTAPI
TfWfpNotify(
    _In_ FWPS_CALLOUT_NOTIFY_TYPE NotifyType,
    _In_ const GUID* FilterKey,
    _Inout_ FWPS_FILTER3* Filter
    )
{
    UNREFERENCED_PARAMETER(NotifyType);
    UNREFERENCED_PARAMETER(FilterKey);
    UNREFERENCED_PARAMETER(Filter);
    return STATUS_SUCCESS;
}

static
VOID
NTAPI
TfWfpFlowDelete(
    _In_ UINT16 LayerId,
    _In_ UINT32 CalloutId,
    _In_ UINT64 FlowContext
    )
{
    UNREFERENCED_PARAMETER(LayerId);
    UNREFERENCED_PARAMETER(CalloutId);
    UNREFERENCED_PARAMETER(FlowContext);
}

NTSTATUS
TfWfpRegisterCallout(
    _In_ PDEVICE_OBJECT DeviceObject
    )
{
    NTSTATUS status;
    FWPS_CALLOUT3 runtimeCallout = { 0 };
    FWPM_SUBLAYER0 subLayer = { 0 };
    FWPM_CALLOUT0 managementCallout = { 0 };
    FWPM_FILTER0 filter = { 0 };
    FWPM_FILTER_CONDITION0 filterCondition = { 0 };

    runtimeCallout.calloutKey = TF_WFP_CALLOUT_GUID;
    runtimeCallout.classifyFn = TfWfpClassifyConnectRedirect;
    runtimeCallout.notifyFn = TfWfpNotify;
    runtimeCallout.flowDeleteFn = TfWfpFlowDelete;

    status = FwpsCalloutRegister3(DeviceObject, &runtimeCallout, &g_TfWfpGlobals.RuntimeCalloutId);
    if (!NT_SUCCESS(status))
    {
        return status;
    }

    status = FwpmEngineOpen0(NULL, RPC_C_AUTHN_WINNT, NULL, NULL, &g_TfWfpGlobals.EngineHandle);
    if (!NT_SUCCESS(status))
    {
        FwpsCalloutUnregisterById0(g_TfWfpGlobals.RuntimeCalloutId);
        g_TfWfpGlobals.RuntimeCalloutId = 0;
        return status;
    }

    subLayer.subLayerKey = TF_WFP_SUBLAYER_GUID;
    subLayer.displayData.name = L"TunnelFlow WFP Redirect Event SubLayer";
    subLayer.flags = 0;
    subLayer.weight = 0x100;

    status = FwpmSubLayerAdd0(g_TfWfpGlobals.EngineHandle, &subLayer, NULL);
    if (!NT_SUCCESS(status) && status != STATUS_FWP_ALREADY_EXISTS)
    {
        TfWfpUnregisterCallout();
        return status;
    }

    managementCallout.calloutKey = TF_WFP_CALLOUT_GUID;
    managementCallout.displayData.name = L"TunnelFlow WFP Redirect Event Callout";
    managementCallout.applicableLayer = FWPM_LAYER_ALE_CONNECT_REDIRECT_V4;

    status = FwpmCalloutAdd0(g_TfWfpGlobals.EngineHandle, &managementCallout, NULL, NULL);
    if (!NT_SUCCESS(status) && status != STATUS_FWP_ALREADY_EXISTS)
    {
        TfWfpUnregisterCallout();
        return status;
    }

    filter.displayData.name = L"TunnelFlow WFP Redirect Event Filter";
    filter.layerKey = FWPM_LAYER_ALE_CONNECT_REDIRECT_V4;
    filter.subLayerKey = TF_WFP_SUBLAYER_GUID;
    filter.weight.type = FWP_EMPTY;
    filter.numFilterConditions = 1;
    filter.filterCondition = &filterCondition;
    filter.action.type = FWP_ACTION_CALLOUT_UNKNOWN;
    filter.action.calloutKey = TF_WFP_CALLOUT_GUID;

    filterCondition.fieldKey = FWPM_CONDITION_IP_PROTOCOL;
    filterCondition.matchType = FWP_MATCH_EQUAL;
    filterCondition.conditionValue.type = FWP_UINT8;
    filterCondition.conditionValue.uint8 = IPPROTO_TCP;

    status = FwpmFilterAdd0(g_TfWfpGlobals.EngineHandle, &filter, NULL, &g_TfWfpGlobals.FilterId);
    if (!NT_SUCCESS(status))
    {
        TfWfpUnregisterCallout();
        return status;
    }

    return STATUS_SUCCESS;
}

VOID
TfWfpUnregisterCallout(
    VOID
    )
{
    if (g_TfWfpGlobals.EngineHandle != NULL)
    {
        if (g_TfWfpGlobals.FilterId != 0)
        {
            FwpmFilterDeleteById0(g_TfWfpGlobals.EngineHandle, g_TfWfpGlobals.FilterId);
            g_TfWfpGlobals.FilterId = 0;
        }

        FwpmCalloutDeleteByKey0(g_TfWfpGlobals.EngineHandle, &TF_WFP_CALLOUT_GUID);
        FwpmSubLayerDeleteByKey0(g_TfWfpGlobals.EngineHandle, &TF_WFP_SUBLAYER_GUID);
        FwpmEngineClose0(g_TfWfpGlobals.EngineHandle);
        g_TfWfpGlobals.EngineHandle = NULL;
    }

    if (g_TfWfpGlobals.RuntimeCalloutId != 0)
    {
        FwpsCalloutUnregisterById0(g_TfWfpGlobals.RuntimeCalloutId);
        g_TfWfpGlobals.RuntimeCalloutId = 0;
    }
}
