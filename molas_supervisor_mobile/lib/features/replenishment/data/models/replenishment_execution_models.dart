class TransferLinePreflightRow {
  const TransferLinePreflightRow({
    required this.sourceItemCode,
    required this.articleNumber,
    required this.sourceItemName,
    required this.targetItemCode,
    required this.targetItemName,
    required this.requestedQty,
    required this.availableQty,
    required this.outcome,
    required this.message,
  });

  final String sourceItemCode;
  final String? articleNumber;
  final String? sourceItemName;
  final String? targetItemCode;
  final String? targetItemName;
  final double requestedQty;
  final double availableQty;
  final String outcome;
  final String? message;

  factory TransferLinePreflightRow.fromJson(Map<String, dynamic> json) {
    return TransferLinePreflightRow(
      sourceItemCode: json['sourceItemCode']?.toString() ?? '',
      articleNumber: json['articleNumber']?.toString(),
      sourceItemName: json['sourceItemName']?.toString(),
      targetItemCode: json['targetItemCode']?.toString(),
      targetItemName: json['targetItemName']?.toString(),
      requestedQty: _readDouble(json['requestedQty']),
      availableQty: _readDouble(json['availableQty']),
      outcome: json['outcome']?.toString() ?? '',
      message: json['message']?.toString(),
    );
  }
}

class ReplenishmentExecutionResult {
  const ReplenishmentExecutionResult({
    required this.transferRef,
    required this.status,
    required this.goodsIssueDocEntry,
    required this.goodsIssueDocNum,
    required this.goodsReceiptDocEntry,
    required this.goodsReceiptDocNum,
    required this.errorMessage,
    required this.lines,
  });

  final String transferRef;
  final String status;
  final int? goodsIssueDocEntry;
  final String? goodsIssueDocNum;
  final int? goodsReceiptDocEntry;
  final String? goodsReceiptDocNum;
  final String? errorMessage;
  final List<TransferLinePreflightRow> lines;

  factory ReplenishmentExecutionResult.fromJson(Map<String, dynamic> json) {
    final rawLines = (json['lines'] as List<dynamic>? ?? const []);

    return ReplenishmentExecutionResult(
      transferRef: json['transferRef']?.toString() ?? '',
      status: json['status']?.toString() ?? '',
      goodsIssueDocEntry: _readNullableInt(json['goodsIssueDocEntry']),
      goodsIssueDocNum: json['goodsIssueDocNum']?.toString(),
      goodsReceiptDocEntry: _readNullableInt(json['goodsReceiptDocEntry']),
      goodsReceiptDocNum: json['goodsReceiptDocNum']?.toString(),
      errorMessage: json['errorMessage']?.toString(),
      lines: rawLines
          .whereType<Map<String, dynamic>>()
          .map(TransferLinePreflightRow.fromJson)
          .toList(),
    );
  }
}

class ExecutionReportRow {
  const ExecutionReportRow({
    required this.requestRef,
    required this.status,
    required this.transferRef,
    required this.goodsIssueDocNum,
    required this.goodsReceiptDocNum,
    required this.executedBySapUser,
    required this.executedAt,
    required this.sourceWarehouse,
    required this.targetWarehouse,
  });

  final String requestRef;
  final String status;
  final String? transferRef;
  final String? goodsIssueDocNum;
  final String? goodsReceiptDocNum;
  final String? executedBySapUser;
  final DateTime? executedAt;
  final String sourceWarehouse;
  final String targetWarehouse;

  factory ExecutionReportRow.fromJson(Map<String, dynamic> json) {
    return ExecutionReportRow(
      requestRef: json['requestRef']?.toString() ?? '',
      status: json['status']?.toString() ?? '',
      transferRef: json['transferRef']?.toString(),
      goodsIssueDocNum: json['goodsIssueDocNum']?.toString(),
      goodsReceiptDocNum: json['goodsReceiptDocNum']?.toString(),
      executedBySapUser: json['executedBySapUser']?.toString(),
      executedAt: _readDateTime(json['executedAt']),
      sourceWarehouse: json['sourceWarehouse']?.toString() ?? '',
      targetWarehouse: json['targetWarehouse']?.toString() ?? '',
    );
  }
}

class ExecutionReportResponse {
  const ExecutionReportResponse({
    required this.rows,
    required this.hasMore,
  });

  final List<ExecutionReportRow> rows;
  final bool hasMore;

  factory ExecutionReportResponse.fromJson(Map<String, dynamic> json) {
    final rawRows = (json['rows'] as List<dynamic>? ?? const []);

    return ExecutionReportResponse(
      rows: rawRows
          .whereType<Map<String, dynamic>>()
          .map(ExecutionReportRow.fromJson)
          .toList(),
      hasMore: json['hasMore'] == true,
    );
  }
}

int? _readNullableInt(dynamic value) {
  if (value == null) return null;
  if (value is int) return value;
  if (value is num) return value.toInt();
  return int.tryParse(value.toString());
}

double _readDouble(dynamic value) {
  if (value is double) return value;
  if (value is num) return value.toDouble();
  return double.tryParse(value?.toString() ?? '') ?? 0;
}

DateTime? _readDateTime(dynamic value) {
  if (value == null) return null;
  return DateTime.tryParse(value.toString());
}
