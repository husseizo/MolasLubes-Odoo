class RequestSummary {
  final String requestRef;
  final String status;
  final String requestedBySapUser;
  final int lineCount;
  final DateTime createdAt;
  final String? submittedAt;
  final String? sourceWarehouse;
  final String? targetWarehouse;

  const RequestSummary({
    required this.requestRef,
    required this.status,
    required this.requestedBySapUser,
    required this.lineCount,
    required this.createdAt,
    this.submittedAt,
    this.sourceWarehouse,
    this.targetWarehouse,
  });

  factory RequestSummary.fromJson(Map<String, dynamic> j) => RequestSummary(
        requestRef: j['requestRef'] as String,
        status: j['status'] as String,
        requestedBySapUser: j['requestedBySapUser'] as String? ?? '',
        lineCount: (j['lines'] as List?)?.length ?? 0,
        createdAt: DateTime.parse(j['createdAt'] as String),
        submittedAt: j['submittedAt'] as String?,
        sourceWarehouse: j['sourceWarehouse'] as String?,
        targetWarehouse: j['targetWarehouse'] as String?,
      );
}
