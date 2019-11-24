# frozen_string_literal: true

# A job for making sure all correct background tasks are queued once
class StartTasksJob < ApplicationJob
  queue_as :default

  def perform
    puts 'Checking if all background jobs are queued...'

    lfs_size_scheduled = Sidekiq::ScheduledSet.new.any? { |job|
      job.display_class == 'UpdateLfsSizesJob'
    }
    UpdateLfsSizesJob.set(wait: 15.minutes).perform_later unless lfs_size_scheduled
    puts "LFS Sizes was queued: #{lfs_size_scheduled}"
    puts 'Done checking'
  end
end
