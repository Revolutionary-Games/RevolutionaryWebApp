# frozen_string_literal: true

# Shows a single report information
class ReportView < HyperComponent
  include Hyperstack::Router::Helpers

  before_mount do
    @show_make_duplicate_of = false
    @duplicate_of_id = ''
    @duplicate_error = nil

    @notes_error = ''
    @editing_notes = false
    @edited_text = ''

    @show_solve = false
    @solved_error = nil
    @solve_text = ''
    @unsolve_modal = false

    @show_callstack = true
    @show_logs = true
    @show_dump = true
    @show_duplicates = true
  end

  def apply_make_duplicate_of
    target = nil
    begin
      target = Integer(@duplicate_of_id)
    rescue StandardError
      mutate @duplicate_error = "Invalid id given: '#{@duplicate_of_id}'"
      return
    end

    UpdateReportDuplicateStatus.run(report_id: @report.id, duplicate_of_id: target).then {
      mutate {
        @show_make_duplicate_of = false
        @duplicate_of_id = ''
        @duplicate_error = 'Duplicate status updated'
      }
    }.fail { |error|
      mutate @duplicate_error = "Error when updating report: #{error}"
    }
  end

  def clear_duplicate_status
    UpdateReportDuplicateStatus.run(report_id: @report.id, duplicate_of_id: nil).then {
      mutate {
        @show_make_duplicate_of = false
        @duplicate_of_id = ''
        @duplicate_error = 'Duplicate status updated'
      }
    }.fail { |error|
      mutate @duplicate_error = "Error when updating report: #{error}"
    }
  end

  def mark_solved(solved, text)
    UpdateReportSolvedStatus.run(report_id: @report.id, solved: solved, solve_text: text)
                            .then {
      mutate {
        @show_solve = false
        @solved_error = nil
      }
    }.fail { |error|
      mutate @solved_error = "Error when updating report: #{error}"
    }
  end

  def finish_editing_notes
    UpdateReportNotes.run(report_id: @report.id, notes: @edited_text).then {
      mutate @editing_notes = false
    }.fail { |error|
      mutate @notes_error = "Error when updating report: #{error}"
    }
  end

  def duplicate_component
    SPAN { 'Duplicate of: ' }

    unless @report.duplicate_of.nil?
      Link("/report/#{@report.duplicate_of.id}") { "Report #{@report.duplicate_of.id}" }
      SPAN { ' ' }
      ReactStrap.Button(color: 'warning') { 'Clear duplicate status' }.on(:click) {
        clear_duplicate_status
      }
    end

    P { @duplicate_error } if @duplicate_error

    return unless @report.duplicate_of.nil?

    if @show_make_duplicate_of
      BR {}
      duplicate_input = INPUT(placeholder: 'id of report this is a duplicate of',
                              value: @duplicate_of_id)

      duplicate_input.on(:enter) do |_event|
        apply_make_duplicate_of
      end
      duplicate_input.on(:change) do |event|
        mutate @duplicate_of_id = event.target.value
      end

      ReactStrap.Button(disabled: @duplicate_of_id.nil? || @duplicate_of_id.empty?,
                        color: 'primary') {
        'Apply'
      }.on(:click) {
        apply_make_duplicate_of
      }
      ReactStrap.Button(color: 'secondary') { 'Cancel' }.on(:click) {
        mutate {
          @show_make_duplicate_of = false
          @duplicate_of_id = ''
        }
      }
    elsif App.acting_user&.developer?
      ReactStrap.Button { 'Mark as duplicate' }.on(:click) {
        mutate @show_make_duplicate_of = true
      }
    end
  end

  def solve_component
    SPAN { 'Solved: ' + (@report.solved ? 'yes' : 'no') }

    P { @solved_error } if @solved_error

    toggle = lambda {
      mutate @unsolve_modal = !@unsolve_modal
    }

    RS.Modal(isOpen: @unsolve_modal, toggle: toggle) {
      RS.ModalHeader(toggle: toggle) { 'Really mark unsolved?' }
      RS.ModalBody {
        'Marking a report as unsolved should only be done in special circumstances. ' \
        'Duplicates of this report won\'t be marked unsolved.'
      }
      RS.ModalFooter {
        RS.Button(color: 'danger') { 'Mark Unsolved' }.on(:click) {
          mutate @unsolve_modal = false
          mark_solved false, @report.solved_comment
        }
        RS.Button(color: 'secondary') { 'Cancel' }.on(:click) {
          mutate @unsolve_modal = false
        }
      }
    }

    if @show_solve
      RS.Input(type: :text, value: @solve_text,
               placeholder: 'Enter reason why this is solved').on(:change) { |e|
        mutate @solve_text = e.target.value
      }

      RS.Button(color: 'primary', disabled: @solve_text.empty?) { 'Solve' }.on(:click) {
        mark_solved true, @solve_text
      }
      RS.Button(color: 'danger') { 'Cancel' }.on(:click) {
        mutate @show_solve = false
      }
    elsif App.acting_user&.developer?
      if @report.solved
        RS.Button(color: 'danger', size: 'sm') { 'Mark as unsolved' }.on(:click) {
          mutate @unsolve_modal = true
        }
      else
        RS.Button(color: 'secondary') { 'Solve' }.on(:click) {
          mutate {
            @show_solve = true
            @solve_text = @report.solved_comment || ''
          }
        }
      end
    end
  end

  render(DIV) do
    @report = Report.find_by_id match.params[:id]

    unless @report
      H1 { "No report exists with id = #{match.params[:id]}" }
      return
    end

    H1 { "Report #{@report.id}" }

    # H1 { "You are not allowed to view this report or it doesn't exist"} if !report.????

    UL {
      LI { "Game Version: #{@report.game_version}" }
      LI { 'Public: ' + (@report.public ? 'yes' : 'no') }
      LI { "Updated At: #{@report.updated_at}" }
      LI { "Created At: #{@report.created_at}" }
      LI { "Crash Time: #{@report.crash_time}" }
      LI { solve_component }
      LI { "Solve comment: #{@report.solved_comment}" }
      LI { "Reporter IP: #{@report.reporter_ip}" } if App.acting_user&.developer?
      LI { "Reporter Email: #{@report.reporter_email}" } if App.acting_user&.admin?
      LI { duplicate_component }
    }

    if !@report.duplicates.nil? && !@report.duplicates.empty?
      H2 {
        SPAN(style: { marginRight: '5px' }) { 'Duplicates of this report' }
        ReactStrap.Button(color: 'secondary') { @show_duplicates ? 'Hide' : 'Show' }
                  .on(:click) {
          mutate @show_duplicates = !@show_duplicates
        }
      }

      ReactStrap.Collapse(isOpen: @show_duplicates) {
        UL {
          @report.duplicates.each { |duplicate|
            LI { Link("/report/#{duplicate.id}") { duplicate.id.to_s } }
          }
        }
      }
    end

    H2 { 'Description' }
    P { @report.description }
    P { @report.extra_description }

    H2 {
      SPAN(style: { marginRight: '25px' }) { 'Notes' }
      if App.acting_user&.developer?
        RS.Button(color: 'secondary', size: 'sm') { 'Edit' }.on(:click) {
          mutate {
            @edited_text = @report.notes
            @editing_notes = true
          }
        }
      end
    }
    P { @notes_error } if @notes_error

    if @editing_notes

      RS.Input(type: :textarea, value: @edited_text,
               placeholder: 'Enter notes to display here').on(:change) { |e|
        mutate @edited_text = e.target.value
      }

      RS.Button(color: 'primary') { 'Save' }.on(:click) {
        finish_editing_notes
      }
      RS.Button(color: 'danger') { 'Cancel' }.on(:click) {
        mutate @editing_notes = false
      }

    else
      P { PRE { @report.notes } }
    end

    H2 {
      SPAN(style: { marginRight: '5px' }) { 'Callstack' }
      ReactStrap.Button(color: 'secondary') { @show_callstack ? 'Hide' : 'Show' }.on(:click) {
        mutate @show_callstack = !@show_callstack
      }
    }

    ReactStrap.Collapse(isOpen: @show_callstack) {
      PRE { @report.primary_callstack }
    }

    if App.acting_user&.developer?
      H2 {
        SPAN(style: { marginRight: '5px' }) { 'Log files' }
        ReactStrap.Button(color: 'secondary') { @show_logs ? 'Hide' : 'Show' }.on(:click) {
          mutate @show_logs = !@show_logs
        }
      }

      ReactStrap.Collapse(isOpen: @show_logs) {
        PRE { @report.log_files }
      }
    end

    H2 {
      SPAN(style: { marginRight: '5px' }) { 'Full dump' }
      ReactStrap.Button(color: 'secondary') { @show_dump ? 'Hide' : 'Show' }.on(:click) {
        mutate @show_dump = !@show_dump
      }
    }

    ReactStrap.Collapse(isOpen: @show_dump) {
      PRE { @report.processed_dump }
    }
  end
end
