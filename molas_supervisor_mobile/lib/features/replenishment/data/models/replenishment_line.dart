import 'package:freezed_annotation/freezed_annotation.dart';

part 'replenishment_line.freezed.dart';
part 'replenishment_line.g.dart';

/// Trend categories returned by the backend.
enum TrendCategory {
  @JsonValue('FastMoving')
  fastMoving,
  @JsonValue('LowStock')
  lowStock,
  @JsonValue('Normal')
  normal,
  @JsonValue('SlowMoving')
  slowMoving,
  @JsonValue('DeadStock')
  deadStock,
  @JsonValue('Inactive')
  inactive,
}

/// Per-line execution status.
enum LineExecutionStatus {
  @JsonValue('PENDING')
  pending,
  @JsonValue('EXECUTED')
  executed,
  @JsonValue('GI_ISSUED')
  giIssued,
  @JsonValue('FAILED')
  failed,
}

@freezed
class ReplenishmentLine with _$ReplenishmentLine {
  const factory ReplenishmentLine({
    required int id,
    required String articleNumber,
    required String sourceItemCode,
    required String targetItemCode,
    required String itemDescription,
    required double currentStock,
    required double availableSupplierStock,
    required double qtySold30d,
    required double qtySold60d,
    required double qtySold90d,
    required double avgDailySales30d,
    required double daysOfStock,
    required TrendCategory trendCategory,
    required int priority,
    required double suggestedQty,
    double? approvedQty,
    required LineExecutionStatus executionStatus,
    String? executionMessage,
  }) = _ReplenishmentLine;

  factory ReplenishmentLine.fromJson(Map<String, dynamic> json) =>
      _$ReplenishmentLineFromJson(json);
}
