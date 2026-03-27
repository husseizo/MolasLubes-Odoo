class ApprovalReportRow {
  const ApprovalReportRow({
    required this.requestRef,
    required this.status,
    required this.requestedBySapUser,
    required this.approvedBySapUser,
    required this.rejectedBySapUser,
    required this.rejectionReason,
    required this.submittedAt,
    required this.approvedAt,
    required this.rejectedAt,
    required this.comments,
  });

  final String requestRef;
  final String status;
  final String? requestedBySapUser;
  final String? approvedBySapUser;
  final String? rejectedBySapUser;
  final String? rejectionReason;
  final DateTime? submittedAt;
  final DateTime? approvedAt;
  final DateTime? rejectedAt;
  final String? comments;

  factory ApprovalReportRow.fromJson(Map<String, dynamic> json) {
    return ApprovalReportRow(
      requestRef: json['requestRef']?.toString() ?? '',
      status: json['status']?.toString() ?? '',
      requestedBySapUser: json['requestedBySapUser']?.toString(),
      approvedBySapUser: json['approvedBySapUser']?.toString(),
      rejectedBySapUser: json['rejectedBySapUser']?.toString(),
      rejectionReason: json['rejectionReason']?.toString(),
      submittedAt: _readDateTime(json['submittedAt']),
      approvedAt: _readDateTime(json['approvedAt']),
      rejectedAt: _readDateTime(json['rejectedAt']),
      comments: json['comments']?.toString(),
    );
  }
}

class ApprovalReportResponse {
  const ApprovalReportResponse({
    required this.rows,
    required this.hasMore,
    required this.count,
  });

  final List<ApprovalReportRow> rows;
  final bool hasMore;
  final int count;

  factory ApprovalReportResponse.fromJson(Map<String, dynamic> json) {
    final rawRows = (json['rows'] as List<dynamic>? ?? const []);

    return ApprovalReportResponse(
      rows: rawRows
          .whereType<Map<String, dynamic>>()
          .map(ApprovalReportRow.fromJson)
          .toList(),
      hasMore: json['hasMore'] == true,
      count: _readInt(json['count']),
    );
  }
}

DateTime? _readDateTime(dynamic value) {
  if (value == null) return null;
  return DateTime.tryParse(value.toString());
}

int _readInt(dynamic value) {
  if (value is int) return value;
  if (value is num) return value.toInt();
  return int.tryParse(value?.toString() ?? '') ?? 0;
}
