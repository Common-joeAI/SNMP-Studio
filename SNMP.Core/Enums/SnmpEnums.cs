namespace SNMP.Core.Enums;

public enum SnmpVersion { V1, V2c, V3 }

public enum AuthProtocol { None, MD5, SHA1, SHA256, SHA512 }

public enum PrivProtocol { None, DES, AES128, AES256 }

public enum SnmpOperation { Get, Walk, Set }

public enum LogLevel { Debug, Info, Warn, Error }

public enum NegotiationResult { Success, Timeout, AuthFailure, NoResponse, UnsupportedVersion, ContextMismatch, Unknown }

public enum WriteMode { Disabled, Enabled }
