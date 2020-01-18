# frozen_string_literal: true

# A job for making sure all correct background tasks are queued once
class StartTasksJob < ApplicationJob
  queue_as :default

  def perform
    puts 'Checking if all background jobs are queued...'

    set = Sidekiq::ScheduledSet.new

    check_lfs set
    check_patrons set

    puts 'Done checking'
  end

  def check_lfs(set)
    if set.any? { |job|
         job.display_class == 'UpdateLfsSizesJob'
       }
      return
    end

    UpdateLfsSizesJob.set(wait: 15.minutes).perform_later
    puts 'LFS Sizes queued'
  end

  def check_patrons(set)
    if set.any? { |job|
         job.display_class == 'RefreshPatronsJob'
       }
      return
    end

    RefreshPatronsJob.set(wait: 21.minutes).perform_later
    puts 'Patron refresh queued'
  end
end
