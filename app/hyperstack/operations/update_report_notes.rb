# frozen_string_literal: true

# Updates the notes field of a report
# TODO: this could detect conflicts if multiple people edit the same thing
class UpdateReportNotes < Hyperstack::ServerOp
  param :acting_user
  param :report_id
  param :notes

  add_error(:report_id, :does_not_exist, 'report does not exist') {
    !(@report = Report.find_by_id(params.report_id))
  }
  validate { params.acting_user.developer? }

  step {
    @report.notes = params.notes
    @report.save!
  }
end
