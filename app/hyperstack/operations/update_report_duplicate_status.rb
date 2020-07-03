# frozen_string_literal: true

class UpdateReportDuplicateStatus < Hyperstack::ServerOp
  param :acting_user
  param :report_id
  param :duplicate_of_id, nils: true

  add_error(:report_id, :does_not_exist, 'report does not exist') {
    !(@report = Report.find_by_id(params.report_id))
  }
  validate { params.acting_user.developer? }

  validate {
    if params.duplicate_of_id.nil?
      true
    else
      Report.find_by_id(params.duplicate_of_id)
    end
  }

  step {
    @report.duplicate_of_id = params.duplicate_of_id
    @report.save!
  }
end
