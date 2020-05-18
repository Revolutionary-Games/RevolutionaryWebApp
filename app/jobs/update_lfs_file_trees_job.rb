# frozen_string_literal: true

# Updates lfs file trees
class UpdateLfsFileTreesJob < ApplicationJob
  queue_as :default

  after_perform do |_job|
    # Queue again
    UpdateLfsFileTreesJob.set(wait: 1.hour).perform_later
  end

  def perform
    LfsProject.ids.each { |project|
      RefreshLfsProjectFiles.perform_now project
    }
  end
end
