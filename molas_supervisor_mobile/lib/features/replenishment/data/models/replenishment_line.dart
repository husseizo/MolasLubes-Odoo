enum TrendCategory {
  fastMoving('FAST_MOVING'),
  lowStock('LOW_STOCK'),
  normal('NORMAL'),
  slowMoving('SLOW_MOVING'),
  deadStock('DEAD_STOCK'),
  inactive('INACTIVE');

  const TrendCategory(this.apiValue);

  final String apiValue;

  static TrendCategory fromApi(String? value) {
    return TrendCategory.values.firstWhere(
      (item) => item.apiValue == value,
      orElse: () => TrendCategory.inactive,
    );
  }
}

enum LineExecutionStatus {
  pending('PENDING'),
  executed('EXECUTED'),
  giIssued('GI_ISSUED'),
  failed('FAILED');

  const LineExecutionStatus(this.apiValue);

  final String apiValue;

  static LineExecutionStatus fromApi(String? value) {
    return LineExecutionStatus.values.firstWhere(
      (item) => item.apiValue == value,
      orElse: () => LineExecutionStatus.pending,
    );
  }
}

class ReplenishmentLine {
  const ReplenishmentLine({
    required this.id,
    required this.requestId,
    required this.sourceItemCode,
    required this.targetItemCode,
    required this.articleNumber,
    required this.itemName,
    required this.currentStockTarget,
    required this.availableSupplierStock,
    required this.qtySold30d,
    required this.qtySold60d,
    required this.qtySold90d,
    required this.avgDailySales30d,
    required this.daysOfStock,
    required this.suggestedQty,
    required this.trendCategory,
    required this.priority,
    required this.approvedQty,
    required this.executionStatus,
    required this.executionMessage,
  });

  final int id;
  final int requestId;
  final String sourceItemCode;
  final String targetItemCode;
  final String articleNumber;
  final String? itemName;
  final double currentStockTarget;
  final double availableSupplierStock;
  final double qtySold30d;
  final double qtySold60d;
  final double qtySold90d;
  final double avgDailySales30d;
  final double daysOfStock;
  final double suggestedQty;
  final TrendCategory trendCategory;
  final int priority;
  final double? approvedQty;
  final LineExecutionStatus executionStatus;
  final String? executionMessage;

  factory ReplenishmentLine.fromJson(Map<String, dynamic> json) {
    return ReplenishmentLine(
      id: _readInt(json['id']),
      requestId: _readInt(json['requestId']),
      sourceItemCode: json['sourceItemCode']?.toString() ?? '',
      targetItemCode: json['targetItemCode']?.toString() ?? '',
      articleNumber: json['articleNumber']?.toString() ?? '',
      itemName: json['itemName']?.toString(),
      currentStockTarget: _readDouble(json['currentStockTarget']),
      availableSupplierStock: _readDouble(json['availableSupplierStock']),
      qtySold30d: _readDouble(json['qtySold30d']),
      qtySold60d: _readDouble(json['qtySold60d']),
      qtySold90d: _readDouble(json['qtySold90d']),
      avgDailySales30d: _readDouble(json['avgDailySales30d']),
      daysOfStock: _readDouble(json['daysOfStock']),
      suggestedQty: _readDouble(json['suggestedQty']),
      trendCategory: TrendCategory.fromApi(json['trendCategory']?.toString()),
      priority: _readInt(json['priority']),
      approvedQty: _readNullableDouble(json['approvedQty']),
      executionStatus:
          LineExecutionStatus.fromApi(json['executionStatus']?.toString()),
      executionMessage: json['executionMessage']?.toString(),
    );
  }

  static int _readInt(dynamic value) {
    if (value is int) return value;
    if (value is num) return value.toInt();
    return int.tryParse(value?.toString() ?? '') ?? 0;
  }

  static double _readDouble(dynamic value) {
    if (value is double) return value;
    if (value is num) return value.toDouble();
    return double.tryParse(value?.toString() ?? '') ?? 0;
  }

  static double? _readNullableDouble(dynamic value) {
    if (value == null) return null;
    if (value is double) return value;
    if (value is num) return value.toDouble();
    return double.tryParse(value.toString());
  }
}
