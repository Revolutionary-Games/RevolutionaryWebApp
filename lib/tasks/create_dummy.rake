# frozen_string_literal: true

namespace :thrive do
  desc 'Create a dummy report'
  task :create_dummy_report, [:email] => [:environment] do |_task, args|
    Report.create! description: 'Thrive Dummy Crash', crash_time: Time.now,
                   reporter_ip: '127.0.0.1', public: true, processed_dump: 'Dummy report',
                   primary_callstack: 'Dummy report', log_files: 'Log files property',
                   game_version: '0.4.2', reporter_email: args[:email]
  end
end
