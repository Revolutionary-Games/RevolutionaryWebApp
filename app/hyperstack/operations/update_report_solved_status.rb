# frozen_string_literal: true

# Updates the solved status of a report and emails the reporter user if they left their email
class UpdateReportSolvedStatus < Hyperstack::ServerOp
  param :acting_user
  param :report_id
  param :solved, type: Boolean
  params :solve_text

  add_error(:report_id, :does_not_exist, 'report does not exist') {
    !(@report = Report.find_by_id(params.report_id))
  }
  validate { params.acting_user.developer? }

  step {
    @report.solved = params.solved
    @report.solved_comment = params.solve_text
    @report.save!
  }
end
