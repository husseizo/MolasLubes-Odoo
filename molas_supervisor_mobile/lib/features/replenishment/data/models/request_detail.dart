import 'package:molas_supervisor_mobile/features/replenishment/data/models/replenishment_line.dart';
import 'request_summary.dart';

class RequestDetail extends RequestSummary {
  final String? approvedBySapUser;
  final String? rejectedBySapUser;
  final String? rejectionReason;
  final String? executedBySapUser;
  final String? approvedAt;
  final String? rejectedAt;
  final String? executedAt;
  final String? transferRef;
  final int? goodsIssueDocNum;
  final int? goodsReceiptDocNum;
  final List<ReplenishmentLine> lines;

  const RequestDetail({
    required super.requestRef,
    required super.status,
    required super.requestedBySapUser,
    required super.lineCount,
    required super.createdAt,
    super.submittedAt,
    super.sourceWarehouse,
    super.targetWarehouse,
    this.approvedBySapUser,
    this.rejectedBySapUser,
    this.rejectionReason,
    this.executedBySapUser,
    this.approvedAt,
    this.rejectedAt,
    this.executedAt,
    this.transferRef,
    this.goodsIssueDocNum,
    this.goodsReceiptDocNum,
    required this.lines,
  });

  factory RequestDetail.fromJson(Map<String, dynamic> j) {
    final linesList = (j['lines'] as List? ?? [])
        .map((e) => ReplenishmentLine.fromJson(e as Map<String, dynamic>))
        .toList();

    return RequestDetail(
      requestRef: j['requestRef'] as String,
      status: j['status'] as String,
      requestedBySapUser: j['requestedBySapUser'] as String? ?? '',
      lineCount: linesList.length,
      createdAt: DateTime.parse(j['createdAt'] as String),
      submittedAt: j['submittedAt'] as String?,
      sourceWarehouse: j['sourceWarehouse'] as String?,
      targetWarehouse: j['targetWarehouse'] as String?,
      approvedBySapUser: j['approvedBySapUser'] as String?,
      rejectedBySapUser: j['rejectedBySapUser'] as String?,
      rejectionReason: j['rejectionReason'] as String?,
      executedBySapUser: j['executedBySapUser'] as String?,
      approvedAt: j['approvedAt'] as String?,
      rejectedAt: j['rejectedAt'] as String?,
      executedAt: j['executedAt'] as String?,
      transferRef: j['transferRef'] as String?,
      goodsIssueDocNum: j['goodsIssueDocNum'] as int?,
      goodsReceiptDocNum: j['goodsReceiptDocNum'] as int?,
      lines: linesList,
    );
  }
}
