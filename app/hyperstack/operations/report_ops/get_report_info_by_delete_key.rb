# frozen_string_literal: true

module ReportOps
  class GetReportInfoByDeleteKey < Hyperstack::ServerOp
    param acting_user: nil, nils: true
    param :key
    add_error :key, :no_valid_key, 'No valid key provided' do
      !Report.find_by_delete_key(params.key)
    end

    step {
      report = Report.find_by delete_key: params.key
      [report.id, report.created_at, report.description]
    }
  end
end
