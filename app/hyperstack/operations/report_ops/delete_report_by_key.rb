# frozen_string_literal: true

module ReportOps
  class DeleteReportByKey < Hyperstack::ServerOp
    param acting_user: nil, nils: true
    param :key
    add_error :key, :no_valid_key, 'No valid key provided' do
      !Report.find_by_delete_key(params.key)
    end

    step { Report.find_by_delete_key(params.key).destroy }
  end
end
