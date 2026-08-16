// Shared browser globals for QuickShare classic scripts (no modules).
// Augments the DOM Window interface. Loaded as ambient declarations only.

interface ZapDropConfig {
    apiBaseUrl?: string;
    shareCode?: string;
}

interface P2PFileMeta {
    name: string;
    size: number;
    mime: string;
    totalChunks?: number;
    hasPin?: boolean;
    pinHash?: string | null;
}

interface P2PReceivedMeta {
    fileName: string;
    fileSize: number;
    mimeType: string;
    hasPin: boolean;
    pinHash: string | null;
}

type P2PRole = 'sender' | 'receiver';

interface P2PSignalingClient {
    apiBaseUrl: string;
    shareCode: string;
    ws: WebSocket | null;
    role: P2PRole | null;
    reconnectAttempts: number;
    _intentionalClose: boolean;
    onPeerJoined: ((receiverId: string) => void) | null;
    onFileMeta: ((meta: P2PReceivedMeta) => void) | null;
    onRelay: ((type: string, data: unknown, receiverId?: string) => void) | null;
    onPeerLeft: ((message: string, receiverId?: string) => void) | null;
    onError: ((message: string) => void) | null;
    onRegistered: ((role: string) => void) | null;
    onWaiting: ((message: string) => void) | null;
    connect(role: P2PRole, fileMeta?: P2PFileMeta): void;
    send(type: string, payload?: unknown): void;
    sendTo(type: string, payload: unknown, receiverId: string): void;
    close(): void;
}

interface P2PEncryptionModule {
    deriveKey(pin: string, salt: string): Promise<CryptoKey>;
    encryptChunk(chunk: ArrayBuffer | Uint8Array, key: CryptoKey, counter: number): Promise<ArrayBuffer>;
    decryptChunk(chunk: ArrayBuffer | Uint8Array, key: CryptoKey, counter: number): Promise<ArrayBuffer>;
    generateSalt(): string;
    hashPin(pin: string, salt: string): Promise<string>;
}

interface P2PEncryptionAdapter {
    key: CryptoKey | null;
    encrypt(data: ArrayBuffer, key: CryptoKey, counter: number): Promise<ArrayBuffer>;
}

interface P2PDecryptionAdapter {
    key: CryptoKey | null;
    decrypt(data: ArrayBuffer | Uint8Array, key: CryptoKey, counter: number): Promise<ArrayBuffer>;
}

interface P2PFileSender {
    file: File;
    dc: RTCDataChannel;
    encryption: P2PEncryptionAdapter | null;
    _cancelled: boolean;
    onProgress: ((sent: number, total: number) => void) | null;
    onComplete: (() => void) | null;
    onError: ((message: string) => void) | null;
    start(): void;
    cancel(): void;
}

interface P2PFileReceiver {
    dc: RTCDataChannel;
    encryption: P2PDecryptionAdapter | null;
    _cancelled: boolean;
    onProgress: ((received: number, total: number) => void) | null;
    onComplete: (() => void) | null;
    onError: ((message: string) => void) | null;
    onFileInfo: ((meta: P2PFileMeta) => void) | null;
    start(): void;
    cancel(): void;
}

interface P2PPeerConnection {
    pc: RTCPeerConnection | null;
    pendingCandidates: Array<RTCIceCandidateInit | string>;
    remoteSet: boolean;
    onIceCandidate: ((candidate: RTCIceCandidateInit) => void) | null;
    onDataChannel: ((channel: RTCDataChannel) => void) | null;
    onConnectionState: ((state: RTCPeerConnectionState) => void) | null;
    onError: ((message: string) => void) | null;
    createDataChannel(label?: string): RTCDataChannel | null;
    createOffer(): Promise<string>;
    createAnswer(offerSdp: string): Promise<string>;
    setRemoteSdp(sdp: string): Promise<void>;
    addIceCandidate(candidate: RTCIceCandidateInit | string): void;
    close(): void;
}

type P2PNetworkResult = 'same' | 'different' | 'unknown';

interface P2PNamespace {
    SignalingClient: new (apiBaseUrl: string, shareCode: string) => P2PSignalingClient;
    PeerConnection: new () => P2PPeerConnection;
    FileSender: new (file: File, dataChannel: RTCDataChannel, encryption: P2PEncryptionAdapter | null) => P2PFileSender;
    FileReceiver: new (dataChannel: RTCDataChannel, encryption: P2PDecryptionAdapter | null) => P2PFileReceiver;
    encryption: P2PEncryptionModule;
    probePublicAddress: (timeoutMs?: number) => Promise<string | null>;
    compareNetwork: (localAddress: string | null, remoteAddress: string | null) => P2PNetworkResult;
    NETWORK_RESULT: {
        same: 'same';
        different: 'different';
        unknown: 'unknown';
    };
}

interface StreamSaverModule {
    createWriteStream(filename: string, options?: { size?: number; mimeType?: string }): {
        getWriter(): WritableStreamDefaultWriter<Uint8Array>;
    };
}

interface DAUBModule {
    toast(message: string, type?: string): void;
}

interface QRCodeInstance {
    addData(data: string): void;
    make(): void;
    getModuleCount(): number;
    isDark(row: number, col: number): boolean;
}

interface QuickShareAdsConfig {
    adUnitId: string;
    callbackUrl: string;
    antiForgeryToken: string;
    remainingAds: number;
    i18n?: Record<string, string>;
}

interface QuickShareAdsModule {
    init(config: QuickShareAdsConfig): void;
    destroy(): void;
    showRewardedAd(): void;
    verifyReward(token: string, amount: number): void;
}

interface RewardedAdInstance {
    load(): void;
    show(): void;
    destroy(): void;
    onRewardedAdLoaded: (() => void) | null;
    onRewardedAdFailedToLoad: ((error: { message: string }) => void) | null;
    onRewardedAdClosed: (() => void) | null;
    onRewardedAdReward: ((reward: { rewardToken?: string; token?: string; amount?: number }) => void) | null;
}

interface UploadPage {
    otpInput(event: Event, idx: number): void;
    otpKeydown(event: KeyboardEvent, idx: number): void;
    readOtp(): void;
    passwordInput(event: Event): void;
    onDragOver(e: DragEvent): void;
    onDragLeave(e: DragEvent): void;
    onDrop(e: DragEvent): void;
    onFileInput(e: Event): void;
    triggerFileInput(): void;
    toggleTextMode(): void;
    shareText(): void;
    copyLink(): void;
    shareLink(): void;
    cancelUpload(): void;
    resumeUpload(): void;
    reset(): void;
}

interface DownloadPage {
    verifyOtp(): void;
    verifyPassword(): void;
    passwordKeydown(event: KeyboardEvent): void;
    otpInput(event: Event, idx: number): void;
    otpKeydown(event: KeyboardEvent, idx: number): void;
    skipAd(): void;
    toggleZK(): void;
    handleDownload(event?: Event): boolean | void;
    showFullText(): void;
    copyText(): void;
    copySha256(): void;
    sharePage(): void;
}

interface P2pSendPage {
    onFileSelect(e: Event): void;
    onDragOver(e: DragEvent): void;
    onDragLeave(e: DragEvent): void;
    onDrop(e: DragEvent): void;
    copyLink(): void;
    cancel(): void;
    cancelTransfer(): void;
    reset(): void;
    fetchQuota(): void;
}

interface P2pDownloadPage {
    startDownload(): void;
    cancelDownload(): void;
}

interface QsPaste {
    fileFromClipboard(event: ClipboardEvent): File | null;
    isEditable(target: EventTarget | null): boolean;
    takeStashed(): File | null;
}

interface Window {
    ZAPDROP_CONFIG?: ZapDropConfig;
    P2P: P2PNamespace;
    streamSaver?: StreamSaverModule;
    DAUB?: DAUBModule;
    qrcode?: (typeNumber: number, errorCorrectionLevel: string) => QRCodeInstance;
    QuickShareAds?: QuickShareAdsModule;
    adsbygoogle?: { loaded?: boolean };
    google?: {
        ads?: {
            admanager?: {
                RewardedAd?: new (adUnitId: string) => RewardedAdInstance;
            };
        };
    };
    uploadPage?: UploadPage;
    downloadApp?: DownloadPage;
    p2pSend?: P2pSendPage;
    p2pDownload?: P2pDownloadPage;
    qsPaste?: QsPaste;
}