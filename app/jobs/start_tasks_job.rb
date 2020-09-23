# frozen_string_literal: true

# A job for making sure all correct background tasks are queued once
class StartTasksJob < ApplicationJob
  queue_as :default

  def perform
    logger.info 'Checking if all background jobs are queued...'

    set = Sidekiq::ScheduledSet.new

    check_lfs set
    check_patrons set
    check_git_trees set
    check_sso set
    check_devbuild set
    check_devbuild_versions set
    check_dehydrated set

    CreateStandardFoldersIfMissing.set(wait: 32.seconds).perform_later

    logger.info 'Done checking'
  end

  def check_lfs(set)
    if set.any? { |job|
         job.display_class == 'UpdateLfsSizesJob'
       }
      return
    end

    UpdateLfsSizesJob.set(wait: 15.minutes).perform_later
    logger.info 'LFS Sizes queued'
  end

  def check_git_trees(set)
    if set.any? { |job|
         job.display_class == 'UpdateLfsFileTreesJob'
       }
      return
    end

    UpdateLfsFileTreesJob.set(wait: 11.minutes).perform_later
    logger.info 'LFS File trees queued'
  end

  def check_patrons(set)
    if set.any? { |job|
         job.display_class == 'RefreshPatronsJob'
       }
      return
    end

    RefreshPatronsJob.set(wait: 21.minutes).perform_later
    logger.info 'Patron refresh queued'
  end

  def check_sso(set)
    if set.any? { |job|
      job.display_class == 'CheckAllSsoUsers'
    }
      return
    end

    CheckAllSsoUsers.set(wait: 32.minutes).perform_later
    logger.info 'SSO user check queued'
  end

  def check_devbuild_versions(set)
    if set.any? { |job|
      job.display_class == 'DeleteDevBuildOldVersions'
    }
      return
    end

    DeleteDevBuildOldVersions.set(wait: 38.minutes).perform_later
    logger.info 'Check devbuild versions check queued'
  end

  def check_devbuild(set)
    if set.any? { |job|
      job.display_class == 'DeleteOldDevBuilds'
    }
      return
    end

    DeleteOldDevBuilds.set(wait: 41.minutes).perform_later
    logger.info 'Check old devbuilds queued'
  end

  def check_dehydrated(set)
    if set.any? { |job|
      job.display_class == 'DeleteUnusedDehydrated'
    }
      return
    end

    DeleteUnusedDehydrated.set(wait: 51.minutes).perform_later
    logger.info 'Check unused dehydrated queued'
  end
end
