# frozen_string_literal: true

# Calculates LFS project object sizes
class UpdateLfsSizesJob < ApplicationJob
  queue_as :default

  after_perform do |_job|
    # Queue again
    UpdateLfsSizesJob.set(wait: 15.minutes).perform_later
  end

  def perform
    LfsProject.all.each { |project|
      new_count = project.lfs_objects.count
      new_size = project.lfs_objects.sum(:size)

      next unless new_count != project.total_object_count ||
                  new_size != project.total_object_size

      project.total_object_count = new_count
      project.total_object_size = new_size
      project.total_size_updated = Time.now
      project.save
    }
  end
end
