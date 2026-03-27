import 'package:molas_supervisor_mobile/features/replenishment/data/models/replenishment_line.dart';

class RecommendationRow {
  const RecommendationRow({
    required this.sourceItemCode,
    required this.targetItemCode,
    required this.articleNumber,
    required this.itemName,
    required this.currentStockTarget,
    required this.qtySold30d,
    required this.qtySold60d,
    required this.qtySold90d,
    required this.avgDailySales30d,
    required this.availableSupplierStock,
    required this.daysOfStock,
    required this.suggestedQty,
    required this.trendCategory,
    required this.priority,
  });

  final String sourceItemCode;
  final String targetItemCode;
  final String articleNumber;
  final String? itemName;
  final double currentStockTarget;
  final double qtySold30d;
  final double qtySold60d;
  final double qtySold90d;
  final double avgDailySales30d;
  final double availableSupplierStock;
  final double daysOfStock;
  final double suggestedQty;
  final TrendCategory trendCategory;
  final int priority;

  factory RecommendationRow.fromJson(Map<String, dynamic> json) {
    return RecommendationRow(
      sourceItemCode: json['sourceItemCode']?.toString() ?? '',
      targetItemCode: json['targetItemCode']?.toString() ?? '',
      articleNumber: json['articleNumber']?.toString() ?? '',
      itemName: json['itemName']?.toString(),
      currentStockTarget: _readDouble(json['currentStockTarget']),
      qtySold30d: _readDouble(json['qtySold30d']),
      qtySold60d: _readDouble(json['qtySold60d']),
      qtySold90d: _readDouble(json['qtySold90d']),
      avgDailySales30d: _readDouble(json['avgDailySales30d']),
      availableSupplierStock: _readDouble(json['availableSupplierStock']),
      daysOfStock: _readDouble(json['daysOfStock']),
      suggestedQty: _readDouble(json['suggestedQty']),
      trendCategory: TrendCategory.fromApi(json['trendCategory']?.toString()),
      priority: _readInt(json['priority']),
    );
  }
}

class GenerateDraftResult {
  const GenerateDraftResult({
    required this.requestRef,
    required this.rowCount,
    required this.rows,
  });

  final String requestRef;
  final int rowCount;
  final List<RecommendationRow> rows;

  factory GenerateDraftResult.fromJson(Map<String, dynamic> json) {
    final rawRows = (json['rows'] as List<dynamic>? ?? const []);

    return GenerateDraftResult(
      requestRef: json['requestRef']?.toString() ?? '',
      rowCount: _readInt(json['rowCount']),
      rows: rawRows
          .whereType<Map<String, dynamic>>()
          .map(RecommendationRow.fromJson)
          .toList(),
    );
  }
}

class RecommendationReportResponse {
  const RecommendationReportResponse({
    required this.count,
    required this.rows,
  });

  final int count;
  final List<RecommendationRow> rows;

  factory RecommendationReportResponse.fromJson(Map<String, dynamic> json) {
    final rawRows = (json['rows'] as List<dynamic>? ?? const []);

    return RecommendationReportResponse(
      count: _readInt(json['count']),
      rows: rawRows
          .whereType<Map<String, dynamic>>()
          .map(RecommendationRow.fromJson)
          .toList(),
    );
  }
}

int _readInt(dynamic value) {
  if (value is int) return value;
  if (value is num) return value.toInt();
  return int.tryParse(value?.toString() ?? '') ?? 0;
}

double _readDouble(dynamic value) {
  if (value is double) return value;
  if (value is num) return value.toDouble();
  return double.tryParse(value?.toString() ?? '') ?? 0;
}
